namespace MongoFlow;

internal interface IVaultMigrationRunner
{
    Task MigrateAllAsync(CancellationToken cancellationToken);
}
