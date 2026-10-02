using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow;

internal class VaultCollection<TDocument>(
    VaultRuntime runtime,
    CollectionModel<TDocument> model,
    FeatureSet disabled) : IVaultCollection<TDocument>
{
    protected VaultRuntime Runtime { get; } = runtime;

    protected FeatureSet Disabled { get; } = disabled;

    public IMongoCollection<TDocument> MongoCollection => model.MongoCollection;

    public virtual IVaultCollection<TDocument> Without(FeatureKey feature) =>
        new VaultCollection<TDocument>(Runtime, model, Disabled.With(feature));

    public async ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(QueryAsync), filter);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        var queryable = session is null ? model.MongoCollection.AsQueryable() : model.MongoCollection.AsQueryable(session);

        return filter is null ? queryable : queryable.Where(filter);
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

        return filter is null ? aggregate : aggregate.Match(filter);
    }

    public void Add(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new InsertOperation<TDocument>(model, Disabled, document));
    }

    public void AddRange(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(document);
        }
    }

    public void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, null, filter, update, default));
    }

    public void DeleteMany(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, null, filter, default));
    }

    /// <summary>Finds with <paramref name="filter"/>, in the scope's open transaction if there is one.</summary>
    private async ValueTask<IFindFluent<TDocument, TDocument>> FindInSessionAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken)
    {
        var session = await Runtime.GetSessionAsync(cancellationToken);

        return session is null ? model.MongoCollection.Find(filter) : model.MongoCollection.Find(session, filter);
    }

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
