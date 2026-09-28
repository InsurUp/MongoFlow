using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection as built at startup: shared by every vault instance, never changed.</summary>
internal abstract class CollectionModel(PropertyInfo property, Type documentType, Type? keyType, int index,
    IMongoDatabase database, string name, IReadOnlySet<FeatureKey> without) : IVaultCollectionInfo
{
    public PropertyInfo Property { get; } = property;

    public Type DocumentType { get; } = documentType;

    public Type? KeyType { get; } = keyType;

    public string PropertyName => Property.Name;

    /// <summary>The collection's position among the vault's collections.</summary>
    public int Index { get; } = index;

    public IMongoDatabase Database { get; } = database;

    public string Name { get; } = name;

    /// <summary>Features this collection opted out of in configuration.</summary>
    public IReadOnlySet<FeatureKey> Without { get; } = without;

    public abstract CollectionNamespace Namespace { get; }

    public abstract object CreateCollection(VaultRuntime runtime);

    public abstract ValueTask<BulkWriteModel> CreateWriteModelAsync(VaultOperation operation, SaveRun run,
        CancellationToken cancellationToken);

    public abstract Task LoadOriginalsAsync(IReadOnlyList<VaultOperation> operations, SaveRun run,
        CancellationToken cancellationToken);

    /// <summary>Whether a replace or delete that matched nothing means the document changed after it was read.</summary>
    public abstract bool HasConcurrencyToken { get; }

    public abstract Task EnsureCreatedAsync(ISet<string> existing, CancellationToken cancellationToken);

    public abstract Task EnsureIndexesAsync(CancellationToken cancellationToken);
}

internal class CollectionModel<TDocument>(CollectionDefinition<TDocument> definition)
    : CollectionModel(definition.Property, typeof(TDocument), definition.KeyType, definition.Index,
        definition.Collection.Database, definition.Collection.CollectionNamespace.CollectionName, definition.Without)
{
    private readonly QueryFilterEntry<TDocument>[] _filters = definition.Filters.ToArray();
    private readonly bool _hasAsyncFilters = definition.Filters.Any(filter => filter.IsAsync);
    private readonly List<CreateIndexModel<TDocument>> _indexes = definition.Indexes;
    private readonly CreateCollectionOptions<TDocument>? _createOptions = definition.CreateOptions;

    public IMongoCollection<TDocument> MongoCollection { get; } = definition.Collection;

    public override CollectionNamespace Namespace => MongoCollection.CollectionNamespace;

    public ConcurrencyTokenModel<TDocument>? Token { get; protected init; }

    public override bool HasConcurrencyToken => Token is not null;

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
        disabled.Count == 0 ? _filters : _filters.Where(filter => filter.Owner is not { } owner || !disabled.Contains(owner));

    public virtual Expression<Func<TDocument, bool>> KeyFilter(object key) =>
        throw new InvalidOperationException($"{Name} has no key.");

    public virtual object? GetKey(TDocument document) =>
        throw new InvalidOperationException($"{Name} has no key.");

    public override object CreateCollection(VaultRuntime runtime) =>
        new VaultCollection<TDocument>(runtime, this, FeatureKeys.None);

    public override async ValueTask<BulkWriteModel> CreateWriteModelAsync(VaultOperation operation, SaveRun run,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case InsertOperation<TDocument> insert:
                return new BulkWriteInsertOneModel<TDocument>(Namespace, insert.Document!);

            case ReplaceOperation<TDocument> replace:
            {
                var filter = await TargetAsync(replace, replace.Key, null, run, cancellationToken);
                if (Token is not null)
                {
                    filter &= Token.Matches(replace.Document!);
                    run.AddUndo(Token.Increment(replace.Document!));
                }

                return new BulkWriteReplaceOneModel<TDocument>(Namespace, filter, replace.Document!);
            }

            case UpdateOperation<TDocument> update:
            {
                var filter = await TargetAsync(update, update.Key, update.Filter, run, cancellationToken);
                var definition = Token is null ? update.Update : Token.WithIncrement(update.Update);

                return update.IsSetBased
                    ? new BulkWriteUpdateManyModel<TDocument>(Namespace, filter, definition)
                    : new BulkWriteUpdateOneModel<TDocument>(Namespace, filter, definition);
            }

            case DeleteOperation<TDocument> delete:
            {
                var filter = await TargetAsync(delete, delete.Key, delete.Filter, run, cancellationToken);
                if (Token is not null && delete is { IsSetBased: false, Document: { } document })
                {
                    filter &= Token.Matches(document);
                }

                return delete.IsSetBased
                    ? new BulkWriteDeleteManyModel<TDocument>(Namespace, filter)
                    : new BulkWriteDeleteOneModel<TDocument>(Namespace, filter);
            }

            default:
                throw new InvalidOperationException($"{operation.GetType().Name} doesn't belong to {Name}.");
        }
    }

    public override async Task LoadOriginalsAsync(IReadOnlyList<VaultOperation> operations, SaveRun run,
        CancellationToken cancellationToken)
    {
        var filters = new List<FilterDefinition<TDocument>>(operations.Count);
        foreach (var operation in operations)
        {
            filters.Add(await TargetAsync(operation, KeyOf(operation), null, run, cancellationToken));
        }

        var documents = await MongoCollection
            .Find(run.Session, Builders<TDocument>.Filter.Or(filters))
            .ToListAsync(cancellationToken);

        var byKey = new Dictionary<object, TDocument>();
        foreach (var document in documents)
        {
            byKey.TryAdd(GetKey(document)!, document);
        }

        foreach (var operation in operations)
        {
            if (KeyOf(operation) is { } key && byKey.TryGetValue(key, out var original))
            {
                ((VaultOperation<TDocument>)operation).Original = original;
            }
        }
    }

    public override async Task EnsureCreatedAsync(ISet<string> existing, CancellationToken cancellationToken)
    {
        if (!existing.Add(Name))
        {
            return;
        }

        try
        {
            await Database.CreateCollectionAsync(Name, _createOptions, cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code == 48)
        {
            // NamespaceExists: created in the meantime, such as by another vault sharing the collection.
        }
    }

    public override async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexes.Count > 0)
        {
            await MongoCollection.Indexes.CreateManyAsync(_indexes, cancellationToken);
        }
    }

    private async ValueTask<FilterDefinition<TDocument>> TargetAsync(VaultOperation operation, object? key,
        Expression<Func<TDocument, bool>>? filter, SaveRun run, CancellationToken cancellationToken)
    {
        var target = key is null ? filter : KeyFilter(key);
        var combined = FilterExpressions.Combine(target, await run.QueryFilterAsync(this, operation.DisabledFeatures, cancellationToken));

        return combined is null ? FilterDefinition<TDocument>.Empty : combined;
    }

    private static object? KeyOf(VaultOperation operation) => operation switch
    {
        ReplaceOperation<TDocument> replace => replace.Key,
        UpdateOperation<TDocument> update => update.Key,
        DeleteOperation<TDocument> delete => delete.Key,
        _ => null
    };
}

internal sealed class KeyedCollectionModel<TDocument, TKey> : CollectionModel<TDocument>
{
    public KeyedCollectionModel(CollectionDefinition<TDocument> definition,
        KeyModel<TDocument, TKey> key,
        ConcurrencyTokenModel<TDocument>? token)
        : base(definition)
    {
        Key = key;
        Token = token;
    }

    public KeyModel<TDocument, TKey> Key { get; }

    public override Expression<Func<TDocument, bool>> KeyFilter(object key) => Key.Filter((TKey)key);

    public override object? GetKey(TDocument document) => Key.Get(document);

    public override object CreateCollection(VaultRuntime runtime) =>
        new KeyedVaultCollection<TDocument, TKey>(runtime, this, FeatureKeys.None);
}
