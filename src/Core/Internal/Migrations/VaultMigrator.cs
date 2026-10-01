using Microsoft.Extensions.DependencyInjection;
using Semver;

namespace MongoFlow;

internal sealed class VaultMigrator(IServiceProvider services, IEnumerable<IVaultMigrationRunner> runners) : IVaultMigrator
{
    public async Task MigrateAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var runner in runners)
        {
            await runner.MigrateAsync(target: null, cancellationToken);
        }
    }

    public Task MigrateAsync<TVault>(SemVersion? target = null, CancellationToken cancellationToken = default)
        where TVault : MongoVault =>
        services.GetRequiredService<VaultMigrationRunner<TVault>>().MigrateAsync(target, cancellationToken);

    public Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault =>
        services.GetRequiredService<VaultMigrationRunner<TVault>>().GetVersionAsync(cancellationToken);
}
