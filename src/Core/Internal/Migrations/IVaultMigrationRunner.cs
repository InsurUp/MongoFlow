using Semver;

namespace MongoFlow;

internal interface IVaultMigrationRunner
{
    Task MigrateAsync(SemVersion? target, CancellationToken cancellationToken);
}
