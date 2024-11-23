using Semver;

namespace MongoFlow;

public interface IMongoVaultMigrationManager
{
    Task<MigrateResult> MigrateAsync(SemVersion? version = null, CancellationToken cancellationToken = default);
    
    Task<SemVersion?> GetCurrentVersionAsync(CancellationToken cancellationToken = default);
}