using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A vault as built at startup: shared by every instance, never changed. It doesn't know the vault type, because
/// nothing that uses it does: the runtime, collections, operations, save pipeline and migrator work the same for every
/// vault. Filling the vault's typed properties is left to <see cref="VaultModelProvider{TVault}"/>.
/// </summary>
internal sealed class VaultModel(Type vaultType,
    IMongoDatabase database,
    IReadOnlyList<IVaultCollectionInfo> collections,
    IReadOnlyList<InterceptorModel> interceptors,
    MigrationModel? migrations)
{
    public Type VaultType { get; } = vaultType;

    public IMongoDatabase Database { get; } = database;

    public IMongoClient Client => Database.Client;

    public IReadOnlyDictionary<Type, IVaultCollectionInfo> CollectionsByDocument { get; } =
        collections.ToDictionary(collection => collection.DocumentType);

    public IReadOnlyList<InterceptorModel> Interceptors { get; } = interceptors;

    public MigrationModel? Migrations { get; } = migrations;
}
