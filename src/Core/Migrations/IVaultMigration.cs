using Semver;

namespace MongoFlow;

/// <summary>A change to <typeparamref name="TVault"/>'s data or schema, applied once and recorded.</summary>
/// <remarks>
/// Created through DI, in a scope of its own, so it can take services. Its vault comes from the same scope, so writes it
/// queues and doesn't save would be lost; a migration that leaves any fails.
/// </remarks>
public interface IVaultMigration<TVault> where TVault : MongoVault
{
    SemVersion Version { get; }

    string? Description => null;

    /// <summary>
    /// Whether the migration runs in a transaction, as it does by default. Turn it off for work MongoDB doesn't allow in
    /// one, such as creating an index on a collection that has documents, or dropping a collection.
    /// </summary>
    bool UseTransaction => true;

    Task UpAsync(MigrationContext<TVault> context, CancellationToken cancellationToken);

    Task DownAsync(MigrationContext<TVault> context, CancellationToken cancellationToken);
}
