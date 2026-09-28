using Semver;

namespace MongoFlow;

/// <summary>
/// Applies vaults' schema and migrations. Registered as a singleton by <c>AddMongoVault</c>; each call runs in its own
/// DI scope.
/// </summary>
public interface IVaultMigrator
{
    /// <summary>
    /// For every registered vault: creates missing collections (with their <c>CreateWith</c> options), applies pending
    /// migrations up to the highest registered version, then creates declared indexes. Indexes come last so migrations
    /// can fix data before a unique index requires it.
    /// </summary>
    /// <exception cref="MigrationFailedException">A migration failed; its transaction was rolled back.</exception>
    Task MigrateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates one vault up or down to <paramref name="target"/>, or up to its highest registered version. Down
    /// migrations run newest first.
    /// </summary>
    /// <exception cref="MigrationFailedException">A migration failed; its transaction was rolled back.</exception>
    Task MigrateAsync<TVault>(SemVersion? target = null, CancellationToken cancellationToken = default)
        where TVault : MongoVault;

    /// <summary>The highest applied version, or <see langword="null"/> if none has been applied.</summary>
    Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault;
}
