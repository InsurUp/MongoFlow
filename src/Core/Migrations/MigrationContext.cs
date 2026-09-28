using MongoDB.Driver;

namespace MongoFlow;

/// <summary>What a migration works with.</summary>
public abstract class MigrationContext<TVault> where TVault : MongoVault
{
    private protected MigrationContext()
    {
    }

    /// <summary>
    /// The vault, from the migration's own DI scope. Its saves join the migration's transaction. Query filters and
    /// features still apply, so switch off what the migration shouldn't be limited by, such as multi-tenancy.
    /// </summary>
    public abstract TVault Vault { get; }

    public abstract IMongoDatabase Database { get; }

    /// <summary>The session to pass to driver calls. It's in a transaction unless the migration opted out.</summary>
    public abstract IClientSessionHandle Session { get; }
}
