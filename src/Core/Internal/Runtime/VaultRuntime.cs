using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault instance's state: its scope's services, its collections and its queued operations.</summary>
internal sealed class VaultRuntime
{
    private readonly object[] _collections;
    private readonly VaultInterceptor?[] _interceptors;
    private readonly List<VaultOperation> _queue = [];
    private readonly HashSet<object> _inserted = new(ReferenceEqualityComparer.Instance);
    private VaultTransactions? _transactions;

    public VaultRuntime(VaultModel model, IServiceProvider services, MongoVault vault)
    {
        Model = model;
        Services = services;
        Vault = vault;
        _interceptors = new VaultInterceptor?[model.Interceptors.Count];
        _collections = model.Collections.Select(collection => collection.CreateCollection(this)).ToArray();

        vault.Attach(this);
        foreach (var collection in model.Collections)
        {
            model.SetCollection(vault, collection, _collections[collection.Index]);
        }
    }

    public VaultModel Model { get; }

    public IServiceProvider Services { get; }

    public MongoVault Vault { get; }

    public VaultTransactions Transactions => _transactions ??= Services.GetRequiredService<VaultTransactions>();

    public bool IsSaving { get; set; }

    public void Enqueue(VaultOperation operation)
    {
        // Adding the same document twice inserts it once.
        if (operation is { Kind: OperationKind.Insert, Document: { } document } && !_inserted.Add(document))
        {
            return;
        }

        _queue.Add(operation);
    }

    public List<VaultOperation> Drain()
    {
        var operations = _queue.ToList();
        _queue.Clear();
        _inserted.Clear();

        return operations;
    }

    /// <summary>The session reads run in: the scope's open transaction's, or none.</summary>
    public async ValueTask<IClientSessionHandle?> GetSessionAsync(CancellationToken cancellationToken) =>
        Transactions.Active is { } transaction ? await transaction.JoinAsync(Model.Client, cancellationToken) : null;

    public VaultInterceptor GetInterceptor(int index) =>
        _interceptors[index] ??= Model.Interceptors[index].Create(Services);

    public IVaultCollection<TDocument> GetCollection<TDocument>() =>
        (IVaultCollection<TDocument>)_collections[Find(typeof(TDocument)).Index];

    public IVaultCollection<TDocument, TKey> GetCollection<TDocument, TKey>()
    {
        var collection = Find(typeof(TDocument));

        return _collections[collection.Index] as IVaultCollection<TDocument, TKey>
            ?? throw new InvalidOperationException(
                $"{Model.VaultType.Name}.{collection.PropertyName} isn't keyed by {typeof(TKey).Name}.");
    }

    private CollectionModel Find(Type documentType) =>
        Model.CollectionsByDocument.TryGetValue(documentType, out var collection)
            ? collection
            : throw new InvalidOperationException($"{Model.VaultType.Name} declares no collection of {documentType.Name}.");
}
