using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A vault as built at startup: shared by every instance, never changed. It isn't generic over the vault type, because
/// nothing that uses it needs to be: the runtime, collections, operations, save pipeline and migrator work the same for
/// every vault, and collection properties are filled through their <see cref="System.Reflection.PropertyInfo"/>.
/// </summary>
internal sealed class VaultModel(Type vaultType,
    IMongoDatabase database,
    IMongoDatabase sourceDatabase,
    VaultEncryption? encryption,
    IReadOnlyList<IVaultSetup> setups,
    IReadOnlyList<ICollectionModel> collections,
    IReadOnlyList<InterceptorModel> interceptors,
    MigrationModel? migrations,
    bool tracksChanges,
    VaultLogs logs,
    VaultMetrics metrics)
{
    public Type VaultType { get; } = vaultType;

    public IMongoDatabase Database { get; } = database;

    public IMongoClient Client => Database.Client;

    /// <summary>
    /// The database as configured. It's <see cref="Database"/>, unless the vault encrypts: then <see cref="Database"/> is
    /// the same database through a client that encrypts, and this one is through the client the vault was given.
    /// </summary>
    public IMongoDatabase SourceDatabase { get; } = sourceDatabase;

    /// <summary>The vault's encryption, or <see langword="null"/> when it doesn't encrypt.</summary>
    public VaultEncryption? Encryption { get; } = encryption;

    /// <summary>
    /// What the migrator sets up for the vault besides its migrations, in order: its encryption, its collections' declared
    /// settings, indexes and search indexes.
    /// </summary>
    public IReadOnlyList<IVaultSetup> Setups { get; } = setups;

    /// <summary>
    /// Whether saves write with collection commands rather than one client bulk write: the driver refuses client bulk
    /// writes to a client that encrypts.
    /// </summary>
    public bool WritesPerCommand { get; } = database.Client.Settings.AutoEncryptionOptions is not null;

    public IReadOnlyList<ICollectionModel> Collections { get; } = collections;

    public IReadOnlyDictionary<Type, ICollectionModel> CollectionsByDocument { get; } =
        collections.ToDictionary(collection => collection.DocumentType);

    public IReadOnlyList<InterceptorModel> Interceptors { get; } = interceptors;

    /// <summary>The vault's migrations, or <see langword="null"/> when it has none.</summary>
    public MigrationModel? Migrations { get; } = migrations;

    /// <summary>Whether reads on keyed collections track the documents they return, unless a view says otherwise.</summary>
    public bool TracksChanges { get; } = tracksChanges;

    public VaultLogs Logs { get; } = logs;

    /// <summary>The root provider's instruments, shared by its vaults.</summary>
    public VaultMetrics Metrics { get; } = metrics;
}
