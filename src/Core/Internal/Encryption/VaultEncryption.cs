using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A vault's encryption, set by MongoFlow.Encryption while the vault is configured: the database the vault reads and
/// writes through, how each collection with encrypted members encrypts, and what the migrator sets up before migrating.
/// </summary>
internal abstract class VaultEncryption : IVaultSetup
{
    bool IVaultSetup.BeforeMigrations => true;

    /// <summary>
    /// The database the vault reads and writes through, in place of <paramref name="source"/>, the one it was configured
    /// with: the same database, through a client that encrypts. Called once, when the model is built; does no I/O.
    /// </summary>
    public abstract IMongoDatabase Database(IMongoDatabase source);

    /// <summary>How <paramref name="collection"/> encrypts <paramref name="members"/>, which it declared encrypted.</summary>
    public abstract ICollectionEncryption ForCollection<TDocument>(CollectionModel<TDocument> collection,
        IReadOnlyList<EncryptedMember> members);

    /// <summary>
    /// What the migrator does before the vault's migrations, outside a transaction, such as creating encrypted collections
    /// and their keys.
    /// </summary>
    public abstract Task PrepareAsync(VaultModel model,
        CancellationToken cancellationToken);

    Task IVaultSetup.RunAsync(VaultModel model,
        MigrateOptions options,
        CancellationToken cancellationToken) =>
        PrepareAsync(model, cancellationToken);
}
