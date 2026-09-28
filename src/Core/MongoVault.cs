namespace MongoFlow;

public abstract class MongoVault : IMongoVault
{
    private VaultRuntime? _runtime;

    internal VaultRuntime Runtime => _runtime ?? throw new InvalidOperationException(
        $"{GetType().Name} wasn't created by MongoFlow. Register it with AddMongoVault and resolve it from DI.");

    public Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default) =>
        SavePipeline.RunAsync(Runtime, cancellationToken);

    public IVaultCollection<TDocument> Collection<TDocument>() => Runtime.GetCollection<TDocument>();

    public IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>() => Runtime.GetCollection<TDocument, TKey>();

    internal void Attach(VaultRuntime runtime) => _runtime = runtime;
}
