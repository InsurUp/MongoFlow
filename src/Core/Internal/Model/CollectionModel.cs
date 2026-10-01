using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection as built at startup: shared by every vault instance, never changed.</summary>
internal class CollectionModel<TDocument>(CollectionDefinition<TDocument> definition) : ICollectionModel
{
    private readonly ImmutableArray<QueryFilterEntry<TDocument>> _filters = definition.Filters;
    private readonly bool _hasAsyncFilters = definition.Filters.Any(filter => filter.IsAsync);
    private readonly bool _onlyStatic = definition.Filters.All(filter => filter.Static is not null);

    // The static filters joined once, at startup; the whole filter when every filter is static.
    private readonly Expression<Func<TDocument, bool>>? _staticFilter =
        FilterExpressions.Combine([.. definition.Filters.Select(filter => filter.Static)]);

    public Type DocumentType => typeof(TDocument);

    public Type? KeyType { get; } = definition.KeyType;

    public PropertyInfo Property { get; } = definition.Property;

    public string PropertyName => Property.Name;

    public IMongoCollection<TDocument> MongoCollection { get; } = definition.Collection;

    public CollectionNamespace Namespace => MongoCollection.CollectionNamespace;

    /// <summary>Renders the collection's filters and fields the way the driver renders its writes.</summary>
    public RenderArgs<TDocument> RenderArgs { get; } = FilterDocuments.RenderArgs(definition.Collection);

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

        // Every request gets the same filter, so it isn't joined again.
        if (_onlyStatic && disabled.Count == 0)
        {
            return ValueTask.FromResult(_staticFilter);
        }

        if (_hasAsyncFilters)
        {
            return ResolveAsync(services, disabled, cancellationToken);
        }

        // A disabled filter stays null, which Combine skips.
        var resolved = new Expression<Func<TDocument, bool>>?[_filters.Length];

        for (var i = 0; i < _filters.Length; i++)
        {
            if (IsActive(_filters[i], disabled))
            {
                resolved[i] = _filters[i].Resolve(services);
            }
        }

        return ValueTask.FromResult(FilterExpressions.Combine(resolved));
    }

    public BsonDocument Render(Expression<Func<TDocument, bool>> filter) => ((FilterDefinition<TDocument>)filter).Render(RenderArgs);

    public BulkWriteModel CreateWriteModel(InsertOperation<TDocument> insert) =>
        new BulkWriteInsertOneModel<TDocument>(Namespace, insert.Document!);

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(ReplaceOperation<TDocument> replace,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(replace, run, cancellationToken);

        return new BulkWriteReplaceOneModel<TDocument>(Namespace, filter, replace.Document!);
    }

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(UpdateOperation<TDocument> update,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(update, run, cancellationToken, update.Filter);

        return update.IsSetBased
            ? new BulkWriteUpdateManyModel<TDocument>(Namespace, filter, update.Update)
            : new BulkWriteUpdateOneModel<TDocument>(Namespace, filter, update.Update);
    }

    public async ValueTask<BulkWriteModel> CreateWriteModelAsync(DeleteOperation<TDocument> delete,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(delete, run, cancellationToken, delete.Filter);

        return delete.IsSetBased
            ? new BulkWriteDeleteManyModel<TDocument>(Namespace, filter)
            : new BulkWriteDeleteOneModel<TDocument>(Namespace, filter);
    }

    /// <summary>Whether the operation's target, without its condition, matches a stored document.</summary>
    public async Task<bool> TargetExistsAsync(VaultOperation<TDocument> operation,
        SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await FilterAsync(operation, run, null, null, cancellationToken);

        return await MongoCollection.Find(run.Session, filter).Limit(1).AnyAsync(cancellationToken);
    }

    /// <summary>The operation's target, with its condition if it has one.</summary>
    private ValueTask<FilterDefinition<TDocument>> WriteFilterAsync(VaultOperation<TDocument> operation,
        SaveRun run,
        CancellationToken cancellationToken,
        Expression<Func<TDocument, bool>>? filter = null) =>
        FilterAsync(operation, run, filter, operation.Condition, cancellationToken);

    private async ValueTask<Expression<Func<TDocument, bool>>?> ResolveAsync(IServiceProvider services,
        IReadOnlySet<FeatureKey> disabled,
        CancellationToken cancellationToken)
    {
        var resolved = new Expression<Func<TDocument, bool>>?[_filters.Length];

        for (var i = 0; i < _filters.Length; i++)
        {
            if (IsActive(_filters[i], disabled))
            {
                resolved[i] = await _filters[i].ResolveAsync(services, cancellationToken);
            }
        }

        return FilterExpressions.Combine(resolved);
    }

    private static bool IsActive(QueryFilterEntry<TDocument> filter,
        IReadOnlySet<FeatureKey> disabled) =>
        filter.Owner is not { } owner || !disabled.Contains(owner);

    /// <summary>
    /// The operation's key or filter, joined with the query filters it was queued under and <paramref name="condition"/>.
    /// A key is matched as BSON and the query filters are rendered once per save, so a write by key needs no LINQ
    /// translation.
    /// </summary>
    private async ValueTask<FilterDefinition<TDocument>> FilterAsync(VaultOperation<TDocument> operation,
        SaveRun run,
        Expression<Func<TDocument, bool>>? filter,
        BsonDocument? condition,
        CancellationToken cancellationToken)
    {
        var target = operation.Target?.Match() ?? (filter is null ? null : Render(filter));
        var queryFilter = await run.QueryFilterAsync(this, operation.DisabledFeatures, cancellationToken);
        var combined = FilterDocuments.And(target, queryFilter, condition);

        return combined is null ? FilterDefinition<TDocument>.Empty : new BsonDocumentFilterDefinition<TDocument>(combined);
    }
}
