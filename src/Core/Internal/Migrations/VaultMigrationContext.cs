using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultMigrationContext<TVault>(TVault vault, IMongoDatabase database, IClientSessionHandle session)
    : MigrationContext<TVault> where TVault : MongoVault
{
    public override TVault Vault => vault;

    public override IMongoDatabase Database => database;

    public override IClientSessionHandle Session => session;
}
