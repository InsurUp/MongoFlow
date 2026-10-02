using Semver;

namespace MongoFlow;

/// <summary>A change to <typeparamref name="TVault"/>'s data or schema, applied once and recorded.</summary>
/// <remarks>
/// Created through DI, in a scope of its own, so it can take services. Its vault comes from the same scope, so writes it
/// queues and doesn't save would be lost; a migration that leaves any fails.
/// </remarks>
public interface IVaultMigration<TVault> where TVault : MongoVault
{
    /// <summary>
    /// The version it migrates the data to, unique among the vault's migrations. Migrations apply in version order, and
    /// revert in reverse.
    /// </summary>
    SemVersion Version { get; }

    /// <summary>What it changes, recorded with it.</summary>
    string? Description => null;

    /// <summary>
    /// Whether the migration runs in a transaction, as it does by default. Turn it off for work MongoDB doesn't allow in
    /// one, such as creating an index on a collection that has documents, or dropping a collection.
    /// </summary>
    bool UseTransaction => true;

    /// <summary>Applies the change.</summary>
    Task UpAsync(MigrationContext<TVault> context, CancellationToken cancellationToken);

    /// <summary>Undoes what <see cref="UpAsync"/> changed, when the vault is migrated below this version.</summary>
    Task DownAsync(MigrationContext<TVault> context, CancellationToken cancellationToken);
}
