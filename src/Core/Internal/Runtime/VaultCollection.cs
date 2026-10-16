using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>
/// A vault collection, typed and untyped: the untyped members check what they're handed and call the typed ones, so both
/// queue the same operations.
/// </summary>
internal class VaultCollection<TDocument>(
    VaultRuntime runtime,
    CollectionModel<TDocument> model,
    FeatureSet disabled) : IVaultCollection<TDocument>, IVaultCollection
{
    protected VaultRuntime Runtime { get; } = runtime;

    protected FeatureSet Disabled { get; } = disabled;

    public Type DocumentType => model.DocumentType;

    public Type? KeyType => model.KeyType;

    public string PropertyName => model.PropertyName;

    public IMongoCollection<TDocument> MongoCollection => model.MongoCollection;

    public IVaultCollection<TDocument> Without(FeatureKey feature) => View(Disabled.With(feature));

    IVaultCollection IVaultCollection.Without(FeatureKey feature) => View(Disabled.With(feature));

    public async ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(QueryAsync), filter);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        // Loaded now, so the query, which may run synchronously, only has to encrypt.
        if (model.Encryption is { } encryption)
        {
            await encryption.EnsureReadyAsync(cancellationToken);
        }

        var queryable = model.Queryable(session);

        var query = filter is null ? queryable : queryable.Where(filter);
        var provider = new VaultQueryProvider<TDocument>(query.GetMongoQueryProvider(),
            model,
            Tracker);

        return new VaultQueryable<TDocument, TDocument>(query, provider);
    }

    public async ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var queryFilter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(FindAsync), queryFilter);

        var combined = FilterExpressions.Combine(filter, queryFilter);

        return await FindInSessionAsync(combined is null ? FilterDefinition<TDocument>.Empty : combined, cancellationToken);
    }

    public async ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var queryFilter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(FindAsync), queryFilter);

        return await FindInSessionAsync(queryFilter is null ? filter : filter & queryFilter, cancellationToken);
    }

    public async ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(AggregateAsync), filter);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        var aggregate = session is null ? model.MongoCollection.Aggregate() : model.MongoCollection.Aggregate(session);

        return filter is null ? aggregate : aggregate.Match(await model.ReadFilterAsync(filter, cancellationToken));
    }

    public void Add(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new InsertOperation<TDocument>(model, Disabled, document) { Tracker = Tracker });
    }

    void IVaultCollection.Add(object document) => Add(DocumentOf(document));

    public void AddRange(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(document);
        }
    }

    void IVaultCollection.AddRange(IEnumerable<object> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(DocumentOf(document));
        }
    }

    public void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);

        QueueUpdateMany(filter, update);
    }

    void IVaultCollection.UpdateMany(BsonDocument filter, BsonValue update)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueUpdateMany(new BsonDocumentFilterDefinition<TDocument>(filter), UpdateOf(update));
    }

    public void DeleteMany(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueDeleteMany(filter);
    }

    void IVaultCollection.DeleteMany(BsonDocument filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueDeleteMany(new BsonDocumentFilterDefinition<TDocument>(filter));
    }

    /// <summary>
    /// Tracks what the reads return, and what the saves insert or replace, when this is a keyed view that tracks changes;
    /// otherwise <see langword="null"/>.
    /// </summary>
    protected virtual IDocumentTracker<TDocument>? Tracker => null;

    /// <summary>A view of the collection with <paramref name="disabled"/> switched off, keyed if this one is.</summary>
    protected virtual VaultCollection<TDocument> View(FeatureSet disabled) => new(Runtime, model, disabled);

    /// <summary><paramref name="document"/>, handed to an untyped member, as a <typeparamref name="TDocument"/>.</summary>
    protected TDocument DocumentOf(object document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document is TDocument typed
            ? typed
            : throw new ArgumentException(
                $"{model.Namespace.CollectionName} holds {typeof(TDocument).Name} documents, so it can't take one of " +
                $"type {document.GetType().Name}.",
                nameof(document));
    }

    /// <summary>
    /// <paramref name="update"/>, handed to an untyped member, as an update definition: a document of update operators, or
    /// an array of pipeline stages.
    /// </summary>
    protected static UpdateDefinition<TDocument> UpdateOf(BsonValue update) => UpdateDocuments.Of<TDocument>(update);

    /// <summary>What a find returns: wrapped, to track the documents it returns, when the reads track changes.</summary>
    protected IFindFluent<TDocument, TDocument> Tracked(IFindFluent<TDocument, TDocument> find) =>
        Tracker is { } tracker ? new TrackingFindFluent<TDocument>(find, tracker) : find;

    /// <summary>Finds with <paramref name="filter"/>, in the scope's open transaction if there is one.</summary>
    private async ValueTask<IFindFluent<TDocument, TDocument>> FindInSessionAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken)
    {
        var session = await Runtime.GetSessionAsync(cancellationToken);
        filter = await model.ReadFilterAsync(filter, cancellationToken);

        return Tracked(session is null ? model.MongoCollection.Find(filter) : model.MongoCollection.Find(session, filter));
    }

    private void QueueUpdateMany(FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update) =>
        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, null, filter, update, default));

    private void QueueDeleteMany(FilterDefinition<TDocument> filter) =>
        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, null, filter, default));

    private void LogReading(string method,
        Expression<Func<TDocument, bool>>? queryFilter)
    {
        var log = Runtime.Model.Logs.Query;
        if (log.IsEnabled(LogLevel.Trace))
        {
            log.Reading(method, model.Namespace.CollectionName, queryFilter?.ToString() ?? "none");
        }
    }
}
