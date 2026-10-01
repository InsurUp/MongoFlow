using Semver;

namespace MongoFlow;

/// <summary>
/// Applies vaults' migrations. Registered as a singleton by <c>AddMongoVault</c>. Each migration runs in a DI scope of
/// its own, in a transaction unless it opts out, and is recorded in the same transaction.
/// </summary>
/// <remarks>
/// Run it from one instance of the application, such as at startup before it serves requests. Instances migrating at once
/// can apply a migration twice: a migration in a transaction fails to record the second time and rolls back, but one
/// without a transaction has already run.
/// </remarks>
public interface IVaultMigrator
{
    /// <summary>For every registered vault, applies its pending migrations, up to its highest version.</summary>
    /// <exception cref="MigrationFailedException">A migration failed; what it did in its transaction was rolled back.</exception>
    /// <exception cref="VaultConfigurationException">
    /// Two migrations of a vault have the same version, or two vaults record their migrations in the same collection.
    /// </exception>
    Task MigrateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates one vault to <paramref name="target"/>, or to its highest version. Migrations up to the target that
    /// haven't been applied are, oldest first, including ones added below the current version; applied migrations above
    /// it are reverted, newest first.
    /// </summary>
    /// <inheritdoc cref="MigrateAllAsync" path="/exception"/>
    /// <exception cref="InvalidOperationException"><typeparamref name="TVault"/> isn't registered with <c>AddMongoVault</c>.</exception>
    Task MigrateAsync<TVault>(SemVersion? target = null, CancellationToken cancellationToken = default)
        where TVault : MongoVault;

    /// <summary>The highest applied version, or <see langword="null"/> when none has been applied.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TVault"/> isn't registered with <c>AddMongoVault</c>.</exception>
    Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault;
}
