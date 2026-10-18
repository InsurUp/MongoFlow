namespace MongoFlow;

/// <summary>How <see cref="IVaultMigrator"/> migrates, beyond the vaults' migrations themselves.</summary>
public sealed class MigrateOptions
{
    /// <summary>
    /// Whether the migrator waits until the search indexes it created or updated, and any declared still building, are
    /// built and queryable, so searches find what's stored once it returns; one that fails to build fails it.
    /// <see langword="true"/> by default.
    /// </summary>
    /// <remarks>
    /// Without waiting, it returns once they're created or updated, sooner over a large collection: a created index finds
    /// nothing until it's built, an updated one is searched as it was until it's rebuilt, and one that fails to build
    /// doesn't fail the migrator. <see cref="SearchIndexManagerExtensions.WaitUntilQueryableAsync"/> waits for one later.
    /// </remarks>
    public bool WaitForSearchIndexes { get; init; } = true;

    /// <summary>
    /// Whether the migrator makes the indexes and search indexes of every collection the migrated vaults declare exactly
    /// what the vaults registered declare of it, once the migrations have run: as well as creating what's missing and
    /// changing in place what MongoDB can, it makes an index unique in place, recreates one that differs in what MongoDB
    /// can't change in place, and drops, last, the indexes no vault declares, but <c>_id_</c> and Queryable Encryption's.
    /// <see langword="false"/> by default: an index no vault declares is left, and one MongoDB can't change in place fails
    /// the migrator.
    /// </summary>
    /// <remarks>
    /// It plans every collection before it changes any, and fails, changing nothing, when two vaults declare an index of one
    /// name otherwise, or when an index it would drop is the only one on a sharded collection's shard key. Each index it
    /// drops is logged with its definition, so it can be created again by hand. Making an index unique fails, leaving it
    /// as it was, when documents share its keys.
    /// </remarks>
    public bool SyncIndexes { get; init; }
}
