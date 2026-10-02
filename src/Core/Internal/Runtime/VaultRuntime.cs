using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Prest;

namespace MongoFlow;

/// <summary>A vault instance's state: its scope's services, its queued operations and its tracked documents.</summary>
internal sealed class VaultRuntime : IDisposable
{
    private readonly VaultInterceptor?[] _interceptors;

    // Writes can be queued from parallel tasks, such as reads that each queue an update. Unguarded, two of them could lose
    // a write, spin forever in the insert set, or write into a pooled array already handed to another request.
    private readonly Lock _queueLock = new();

    // Pooled, and rented on first use, so a vault instance that only reads rents nothing. The save that drains the queue
    // owns it from then on and gives it back when the save ends.
    private PooledList<VaultOperation>? _queue;
    private ComparerSwissHashSet<object>? _inserted;
    private ChangeTracker? _tracker;
    private int _saving;

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

    /// <summary>The documents the instance's reads tracked, created by the first read that tracks one.</summary>
    public ChangeTracker Tracker => LazyInitializer.EnsureInitialized(ref _tracker, static () => new ChangeTracker());

    /// <summary>
    /// Marks the vault as saving, or returns <see langword="false"/> when it already is: from its own interceptors, or from
    /// a parallel task.
    /// </summary>
    public bool TryStartSaving() => Interlocked.Exchange(ref _saving, 1) == 0;

    public void EndSaving() => Volatile.Write(ref _saving, 0);

    public void Enqueue(VaultOperation operation)
    {
        // Queued by one of this vault's interceptors as its save runs: it joins the save. A write another task queues
        // meanwhile waits for the next save, so it goes through every interceptor.
        if (SaveRun.Current is { IsSaving: true } run && ReferenceEquals(run.Runtime, this))
        {
            run.Add(operation);
            return;
        }

        lock (_queueLock)
        {
            // Adding the same document twice inserts it once.
            if (operation is { Kind: OperationKind.Insert, Document: { } document } &&
                !(_inserted ??= ComparerSwissHashSet<object>.Create(ReferenceEqualityComparer.Instance)).Add(document))
            {
                return;
            }

            (_queue ??= new PooledList<VaultOperation>(clearOnReturn: true)).Add(operation);
        }
    }

    /// <summary>
    /// Hands the queued operations to a save, which disposes the list when it ends, or <see langword="null"/> when nothing
    /// is queued.
    /// </summary>
    public PooledList<VaultOperation>? Drain()
    {
        lock (_queueLock)
        {
            var queue = _queue;
            _queue = null;
            _inserted?.Dispose();
            _inserted = null;

            return queue;
        }
    }

    /// <summary>
    /// Puts an update before the queued <paramref name="operations"/> for each tracked document that changed, creating the
    /// list if nothing was queued. See <see cref="ChangeTracker.DetectChanges"/>.
    /// </summary>
    public TrackedChanges? DetectChanges(ref PooledList<VaultOperation>? operations) =>
        Volatile.Read(ref _tracker)?.DetectChanges(ref operations, Model);

    /// <summary>Whether a tracked document changed since it was read or last saved.</summary>
    public bool HasTrackedChanges() => Volatile.Read(ref _tracker)?.HasChanges() == true;

    /// <summary>The session reads run in: the scope's open transaction's, or none.</summary>
    public async ValueTask<IClientSessionHandle?> GetSessionAsync(CancellationToken cancellationToken) =>
        TransactionManager.Active is { } transaction
            ? await transaction.GetSessionAsync(Model.Client, cancellationToken)
            : null;

    public VaultInterceptor GetInterceptor(int index) =>
        _interceptors[index] ??= Model.Interceptors[index].Create(Services);

    public IVaultCollection<TDocument> GetCollection<TDocument>() =>
        ((CollectionModel<TDocument>)Find(typeof(TDocument))).CreateCollection(this, FeatureSet.Empty);

    public IVaultCollection<TDocument, TKey> GetCollection<TDocument, TKey>()
    {
        var collection = Find(typeof(TDocument));

        return collection is KeyedCollectionModel<TDocument, TKey> keyed
            ? keyed.CreateKeyedCollection(this, FeatureSet.Empty)
            : throw new InvalidOperationException(
                $"{Model.VaultType.Name}.{collection.PropertyName} isn't keyed by {typeof(TKey).Name}.");
    }

    /// <summary>Gives back what the instance rented: its tracked documents' snapshots, and writes queued but never saved.</summary>
    public void Dispose()
    {
        Volatile.Read(ref _tracker)?.Dispose();
        Drain()?.Dispose();
    }

    private ICollectionModel Find(Type documentType) =>
        Model.CollectionsByDocument.TryGetValue(documentType, out var collection)
            ? collection
            : throw new InvalidOperationException($"{Model.VaultType.Name} declares no collection of {documentType.Name}.");
}
