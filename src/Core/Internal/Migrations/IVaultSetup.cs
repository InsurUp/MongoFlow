namespace MongoFlow;

/// <summary>
/// Work the migrator does for a vault besides its migrations, outside any transaction, such as setting up encrypted
/// collections or search indexes.
/// </summary>
internal interface IVaultSetup
{
    /// <summary>
    /// Whether it runs before the vault's migrations, as encrypted collections do, which migrations may write to; or after,
    /// as search indexes do, over collections and fields migrations may create or rename.
    /// </summary>
    bool BeforeMigrations { get; }

    Task RunAsync(VaultModel model,
        MigrateOptions options,
        CancellationToken cancellationToken);
}
