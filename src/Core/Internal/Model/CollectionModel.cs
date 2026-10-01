using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection as built at startup: shared by every vault instance, never changed.</summary>
internal class CollectionModel<TDocument>(CollectionDefinition<TDocument> definition) : IVaultCollectionInfo
{
    private readonly QueryFilterEntry<TDocument>[] _filters = definition.Filters.ToArray();
    private readonly bool _hasAsyncFilters = definition.Filters.Any(filter => filter.IsAsync);

    public Type DocumentType => typeof(TDocument);

    public Type? KeyType { get; } = definition.KeyType;

    public string PropertyName { get; } = definition.PropertyName;

    public IMongoCollection<TDocument> MongoCollection { get; } = definition.Collection;

    public CollectionNamespace Namespace => MongoCollection.CollectionNamespace;

    public ConcurrencyTokenModel<TDocument>? Token { get; protected init; }

    public virtual IVaultCollection<TDocument> CreateCollection(VaultRuntime runtime,
        IReadOnlySet<FeatureKey> disabled) =>
        new VaultCollection<TDocument>(runtime, this, disabled);

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
        if (Token is not null)
        {
            filter &= Guard(replace, replace.Document!, run, increment: true);
        }

        return new BulkWriteReplaceOneModel<TDocument>(Namespace, filter, replace.Document!);
    }

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(UpdateOperation<TDocument> update,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await TargetAsync(update, run, cancellationToken, update.Filter);
        var definition = update.Update;
        if (Token is not null)
        {
            if (update.Document is { } document)
            {
                filter &= Guard(update, document, run, increment: true);
            }

            definition = Token.WithIncrement(definition);
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
        if (Token is not null && delete.Document is { } document)
        {
            filter &= Guard(delete, document, run, increment: false);
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

    /// <summary>
    /// Limits <paramref name="operation"/> to the stored document whose token still has <paramref name="document"/>'s
    /// value, and increments the token on <paramref name="document"/> to match what gets stored, undone if the save fails.
    /// </summary>
    private FilterDefinition<TDocument> Guard(VaultOperation<TDocument> operation,
        TDocument document,
        SaveRun run,
        bool increment)
    {
        var matches = Token!.Matches(document);
        if (increment)
        {
            run.AddUndo(Token.Increment(document));
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
