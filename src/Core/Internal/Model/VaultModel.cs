using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A vault as built at startup: shared by every instance, never changed. It isn't generic over the vault type, because
/// nothing that uses it needs to be: the runtime, collections, operations, save pipeline and migrator work the same for
/// every vault, and collection properties are filled through their <see cref="System.Reflection.PropertyInfo"/>.
/// </summary>
internal sealed class VaultModel(Type vaultType,
    IMongoDatabase database,
    IReadOnlyList<ICollectionModel> collections,
    IReadOnlyList<InterceptorModel> interceptors,
    MigrationModel? migrations,
    VaultLogs logs,
    VaultMetrics metrics)
{
    public Type VaultType { get; } = vaultType;

    public IMongoDatabase Database { get; } = database;

    public IMongoClient Client => Database.Client;

    public IReadOnlyList<ICollectionModel> Collections { get; } = collections;

    public IReadOnlyDictionary<Type, ICollectionModel> CollectionsByDocument { get; } =
        collections.ToDictionary(collection => collection.DocumentType);

    public IReadOnlyList<InterceptorModel> Interceptors { get; } = interceptors;

    /// <summary>The vault's migrations, or <see langword="null"/> when it has none.</summary>
    public MigrationModel? Migrations { get; } = migrations;

    public VaultLogs Logs { get; } = logs;

    /// <summary>The root provider's instruments, shared by its vaults.</summary>
    public VaultMetrics Metrics { get; } = metrics;
}
