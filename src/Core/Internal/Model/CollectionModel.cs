using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection as built at startup: shared by every vault instance, never changed.</summary>
internal class CollectionModel<TDocument>(CollectionDefinition<TDocument> definition) : ICollectionModel
{
    private readonly QueryFilterEntry<TDocument>[] _filters = definition.Filters.ToArray();
    private readonly bool _hasAsyncFilters = definition.Filters.Any(filter => filter.IsAsync);

    public Type DocumentType => typeof(TDocument);

    public Type? KeyType { get; } = definition.KeyType;

    public PropertyInfo Property { get; } = definition.Property;

    public string PropertyName => Property.Name;

    public IMongoCollection<TDocument> MongoCollection { get; } = definition.Collection;

    public CollectionNamespace Namespace => MongoCollection.CollectionNamespace;

    /// <summary>The token of the concurrency token feature, unless the collection opted out.</summary>
    public ConcurrencyTokenModel<TDocument>? Token { get; } = definition.Token;

    public virtual IVaultCollection<TDocument> CreateCollection(VaultRuntime runtime,
        IReadOnlySet<FeatureKey> disabled) =>
        new VaultCollection<TDocument>(runtime, this, disabled);

    // A keyed model's CreateCollection returns a keyed collection, which a keyed property accepts.
    public void Attach(MongoVault vault, VaultRuntime runtime) =>
        Property.SetValue(vault, CreateCollection(runtime, FeatureKeys.None));

    /// <summary>
    /// The collection's query filters joined into one, minus those of <paramref name="disabled"/> features, or
    /// <see langword="null"/> when nothing filters.
    /// </summary>
    public ValueTask<Expression<Func<TDocument, bool>>?> ResolveFilterAsync(IServiceProvider services,
        IReadOnlySet<FeatureKey> disabled,
        CancellationToken cancellationToken)
    {
        if (_filters.Length == 0)
        {
            return ValueTask.FromResult<Expression<Func<TDocument, bool>>?>(null);
        }

        return _hasAsyncFilters
            ? ResolveAsync(services, disabled, cancellationToken)
            : ValueTask.FromResult(FilterExpressions.Combine(Active(disabled).Select(filter => filter.Resolve(services))));
    }

    public BulkWriteModel CreateWriteModel(InsertOperation<TDocument> insert) =>
        new BulkWriteInsertOneModel<TDocument>(Namespace, insert.Document!);

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(ReplaceOperation<TDocument> replace,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await TargetAsync(replace, run, cancellationToken);
        if (TokenFor(replace) is { } token)
        {
            filter &= Guard(token, replace, replace.Document!, run, increment: true);
        }

        return new BulkWriteReplaceOneModel<TDocument>(Namespace, filter, replace.Document!);
    }

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(UpdateOperation<TDocument> update,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await TargetAsync(update, run, cancellationToken, update.Filter);
        var definition = update.Update;
        if (TokenFor(update) is { } token)
        {
            if (update.Document is { } document)
            {
                filter &= Guard(token, update, document, run, increment: true);
            }

            definition = token.WithIncrement(definition);
        }

        return update.IsSetBased
            ? new BulkWriteUpdateManyModel<TDocument>(Namespace, filter, definition)
            : new BulkWriteUpdateOneModel<TDocument>(Namespace, filter, definition);
    }

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(DeleteOperation<TDocument> delete,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await TargetAsync(delete, run, cancellationToken, delete.Filter);
        if (TokenFor(delete) is { } token && delete.Document is { } document)
        {
            filter &= Guard(token, delete, document, run, increment: false);
        }

        return delete.IsSetBased
            ? new BulkWriteDeleteManyModel<TDocument>(Namespace, filter)
            : new BulkWriteDeleteOneModel<TDocument>(Namespace, filter);
    }

    public async Task<bool> TargetExistsAsync(VaultOperation<TDocument> operation,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await TargetAsync(operation, run, cancellationToken);

        return await MongoCollection.Find(run.Session, filter).Limit(1).AnyAsync(cancellationToken);
    }

    /// <summary>The token, unless the feature was switched off on the view <paramref name="operation"/> was queued through.</summary>
    private ConcurrencyTokenModel<TDocument>? TokenFor(VaultOperation<TDocument> operation) =>
        Token is not null && !operation.DisabledFeatures.Contains(ConcurrencyTokenFeature.Key) ? Token : null;

    /// <summary>
    /// Limits <paramref name="operation"/> to the stored document whose token still has <paramref name="document"/>'s
    /// value, and increments the token on <paramref name="document"/> to match what gets stored, undone if the save fails.
    /// </summary>
    private static FilterDefinition<TDocument> Guard(ConcurrencyTokenModel<TDocument> token,
        VaultOperation<TDocument> operation,
        TDocument document,
        SaveRun run,
        bool increment)
    {
        var matches = token.Matches(document);
        if (increment)
        {
            run.AddUndo(token.Increment(document));
        }

        operation.IsGuarded = true;

        return matches;
    }

    private async ValueTask<Expression<Func<TDocument, bool>>?> ResolveAsync(IServiceProvider services,
        IReadOnlySet<FeatureKey> disabled,
        CancellationToken cancellationToken)
    {
        var resolved = new List<Expression<Func<TDocument, bool>>?>();

        foreach (var filter in Active(disabled))
        {
            resolved.Add(await filter.ResolveAsync(services, cancellationToken));
        }

        return FilterExpressions.Combine(resolved);
    }

    private IEnumerable<QueryFilterEntry<TDocument>> Active(IReadOnlySet<FeatureKey> disabled) =>
        disabled.Count == 0
            ? _filters
            : _filters.Where(filter => filter.Owner is not { } owner || !disabled.Contains(owner));

    /// <summary>The operation's key or filter, joined with the query filters it was queued under.</summary>
    private async ValueTask<FilterDefinition<TDocument>> TargetAsync(VaultOperation<TDocument> operation,
        SaveRun run,
        CancellationToken cancellationToken,
        Expression<Func<TDocument, bool>>? filter = null)
    {
        var target = operation.Target?.Filter() ?? filter;
        var queryFilter = await run.QueryFilterAsync(this, operation.DisabledFeatures, cancellationToken);
        var combined = FilterExpressions.Combine(target, queryFilter);

        return combined is null ? FilterDefinition<TDocument>.Empty : combined;
    }
}
