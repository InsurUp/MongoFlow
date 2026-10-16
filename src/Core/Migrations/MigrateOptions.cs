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
}
