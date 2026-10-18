using Semver;

namespace MongoFlow;

/// <summary>
/// Applies vaults' migrations. Registered as a singleton by <c>AddMongoVault</c>. Each migration runs in a DI scope of
/// its own, in a transaction unless it opts out, and is recorded in the same transaction. A vault that encrypts, with
/// MongoFlow.Encryption, has its encrypted collections set up first: created with their data keys, or rebuilt encrypted
/// when they hold documents already. What a vault's collections declare is set up too: their settings before its
/// migrations, their indexes and search indexes after, failing before anything is changed when a collection or an index
/// differs in what MongoDB can't change in place; the migrator waits until search indexes are queryable unless
/// <see cref="MigrateOptions"/> says not to. With <see cref="MigrateOptions.SyncIndexes"/>, the indexes and search indexes
/// of the collections the migrated vaults declare are synced instead, once every vault's migrations have run, with what
/// every registered vault declares of them.
/// </summary>
/// <remarks>
/// Run it from one instance of the application, such as at startup before it serves requests. Instances migrating at once
/// can apply a migration twice: a migration in a transaction fails to record the second time and rolls back, but one
/// without a transaction has already run.
/// </remarks>
public interface IVaultMigrator
{
    /// <summary>
    /// Migrates every registered vault to the version its <see cref="MongoVersionAttribute"/> names, or to its highest:
    /// pending migrations up to it are applied, and applied ones above it reverted.
    /// </summary>
    /// <exception cref="MigrationFailedException">A migration failed; what it did in its transaction was rolled back.</exception>
    /// <exception cref="VaultConfigurationException">
    /// Two migrations of a vault have the same version, a vault's <see cref="MongoVersionAttribute"/> names a version none
    /// of its migrations has, or two vaults record their migrations in the same collection.
    /// </exception>
    Task MigrateAllAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc cref="MigrateAllAsync(CancellationToken)"/>
    /// <param name="options">How it migrates, such as whether it waits for search indexes.</param>
    /// <param name="cancellationToken">Cancels the migrations, and the wait for search indexes.</param>
    Task MigrateAllAsync(MigrateOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates one vault to <paramref name="target"/>, or without one to the version its
    /// <see cref="MongoVersionAttribute"/> names, or to its highest. Migrations up to the target that haven't been applied
    /// are, oldest first, including ones added below the current version; applied migrations above it are reverted,
    /// newest first.
    /// </summary>
    /// <inheritdoc cref="MigrateAllAsync(CancellationToken)" path="/exception"/>
    /// <exception cref="InvalidOperationException"><typeparamref name="TVault"/> isn't registered with <c>AddMongoVault</c>.</exception>
    Task MigrateAsync<TVault>(SemVersion? target = null, CancellationToken cancellationToken = default)
        where TVault : MongoVault;

    /// <inheritdoc cref="MigrateAsync{TVault}(SemVersion?, CancellationToken)"/>
    /// <param name="target">The version to migrate to, or <see langword="null"/> for the vault's own.</param>
    /// <param name="options">How it migrates, such as whether it waits for search indexes.</param>
    /// <param name="cancellationToken">Cancels the migrations, and the wait for search indexes.</param>
    Task MigrateAsync<TVault>(SemVersion? target,
        MigrateOptions options,
        CancellationToken cancellationToken = default)
        where TVault : MongoVault;

    /// <summary>The highest applied version, or <see langword="null"/> when none has been applied.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TVault"/> isn't registered with <c>AddMongoVault</c>.</exception>
    Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault;
}
