using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Prest;

namespace MongoFlow;

/// <summary>A vault instance's state: its scope's services and its queued operations.</summary>
internal sealed class VaultRuntime
{
    private readonly VaultInterceptor?[] _interceptors;

    // Pooled, and rented on first use, so a vault instance that only reads rents nothing. The save that drains the queue
    // owns it from then on and gives it back when the save ends.
    private PooledList<VaultOperation>? _queue;
    private ComparerSwissHashSet<object>? _inserted;

    public VaultRuntime(VaultModel model,
        IServiceProvider services,
        MongoVault vault)
    {
        Model = model;
        Services = services;
        Vault = vault;
        _interceptors = new VaultInterceptor?[model.Interceptors.Count];

        foreach (var collection in model.Collections)
        {
            collection.Attach(vault, this);
        }
    }

    public VaultModel Model { get; }

    public IServiceProvider Services { get; }

    public MongoVault Vault { get; }

    public VaultTransactionManager TransactionManager => field ??= Services.GetRequiredService<VaultTransactionManager>();

    public bool IsSaving { get; set; }

    public void Enqueue(VaultOperation operation)
    {
        // Adding the same document twice inserts it once.
        if (operation is { Kind: OperationKind.Insert, Document: { } document } &&
            !(_inserted ??= ComparerSwissHashSet<object>.Create(ReferenceEqualityComparer.Instance)).Add(document))
        {
            return;
        }

        (_queue ??= new PooledList<VaultOperation>(clearOnReturn: true)).Add(operation);
    }

    /// <summary>
    /// Hands the queued operations to a save, which disposes the list when it ends, or <see langword="null"/> when nothing
    /// is queued.
    /// </summary>
    public PooledList<VaultOperation>? Drain()
    {
        var queue = _queue;
        _queue = null;
        _inserted?.Dispose();
        _inserted = null;

        return queue;
    }

    /// <summary>Moves the operations queued during a save, such as by its interceptors, into the save.</summary>
    public void DrainInto(PooledList<VaultOperation> operations)
    {
        if (Drain() is { } queue)
        {
            operations.AddRange(queue.Span);
            queue.Dispose();
        }
    }

    /// <summary>The session reads run in: the scope's open transaction's, or none.</summary>
    public async ValueTask<IClientSessionHandle?> GetSessionAsync(CancellationToken cancellationToken) =>
        TransactionManager.Active is { } transaction
            ? await transaction.JoinAsync(Model.Client, cancellationToken)
            : null;

    public VaultInterceptor GetInterceptor(int index) =>
        _interceptors[index] ??= Model.Interceptors[index].Create(Services);

    public IVaultCollection<TDocument> GetCollection<TDocument>() =>
        Find(typeof(TDocument)) is CollectionModel<TDocument> collection
            ? collection.CreateCollection(this, FeatureSet.Empty)
            : throw new InvalidOperationException($"{Model.VaultType.Name} declares no collection of {typeof(TDocument).Name}.");

    public IVaultCollection<TDocument, TKey> GetCollection<TDocument, TKey>()
    {
        var collection = Find(typeof(TDocument));

        return collection is KeyedCollectionModel<TDocument, TKey> keyed
            ? keyed.CreateKeyedCollection(this, FeatureSet.Empty)
            : throw new InvalidOperationException(
                $"{Model.VaultType.Name}.{collection.PropertyName} isn't keyed by {typeof(TKey).Name}.");
    }

    private ICollectionModel Find(Type documentType) =>
        Model.CollectionsByDocument.TryGetValue(documentType, out var collection)
            ? collection
            : throw new InvalidOperationException($"{Model.VaultType.Name} declares no collection of {documentType.Name}.");
}
