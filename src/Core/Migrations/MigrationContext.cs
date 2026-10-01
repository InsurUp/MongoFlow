using MongoDB.Driver;

namespace MongoFlow;

/// <summary>What a migration works with.</summary>
public sealed class MigrationContext<TVault> where TVault : MongoVault
{
    internal MigrationContext(TVault vault,
        IMongoDatabase database,
        IClientSessionHandle session)
    {
        Vault = vault;
        Database = database;
        Session = session;
    }

    /// <summary>
    /// The vault, from the migration's own DI scope. Its saves join the migration's transaction. Query filters and
    /// features still apply, so switch off what the migration shouldn't be limited by, such as multi-tenancy.
    /// </summary>
    public TVault Vault { get; }

    public IMongoDatabase Database { get; }

    /// <summary>The session to pass to driver calls. It's in a transaction unless the migration opted out.</summary>
    public IClientSessionHandle Session { get; }
}
