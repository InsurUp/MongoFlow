namespace MongoFlow;

/// <summary>
/// The base of a vault: a unit of work over the collections it declares as <see cref="IVaultCollection{TDocument}"/>
/// and <see cref="IVaultCollection{TDocument, TKey}"/> properties. Register it with <c>AddMongoVault</c>, and resolve
/// it from DI, which fills in its collections.
/// </summary>
public abstract class MongoVault : IMongoVault, IDisposable
{
    private VaultRuntime? _runtime;

    internal VaultRuntime Runtime => _runtime ?? throw new InvalidOperationException(
        $"{GetType().Name} wasn't created by MongoFlow. Register it with AddMongoVault and resolve it from DI.");

    /// <inheritdoc/>
    public Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default) =>
        SavePipeline.RunAsync(Runtime, cancellationToken);

    /// <inheritdoc/>
    public IVaultCollection<TDocument> Collection<TDocument>() => Runtime.GetCollection<TDocument>();

    /// <inheritdoc/>
    public IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>() => Runtime.GetCollection<TDocument, TKey>();

    /// <inheritdoc/>
    public IVaultCollection Collection(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        return Runtime.GetCollection(documentType);
    }

    /// <inheritdoc/>
    public IKeyedVaultCollection KeyedCollection(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        return Runtime.GetKeyedCollection(documentType);
    }

    /// <summary>
    /// Gives back the pooled memory the vault holds: its tracked documents' snapshots, and writes queued but never saved,
    /// which are dropped. Its scope disposes it; afterwards it starts again with nothing tracked or queued.
    /// </summary>
    public void Dispose() => _runtime?.Dispose();

    internal void Attach(VaultRuntime runtime) => _runtime = runtime;
}
