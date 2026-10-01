using Microsoft.Extensions.DependencyInjection;
using Semver;

namespace MongoFlow;

internal sealed class VaultMigrator(IServiceProvider services,
    IEnumerable<IVaultMigrationRunner> runners) : IVaultMigrator
{
    public async Task MigrateAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfHistoriesShared();

        foreach (var runner in runners)
        {
            await runner.MigrateAsync(target: null, cancellationToken);
        }
    }

    public Task MigrateAsync<TVault>(SemVersion? target = null,
        CancellationToken cancellationToken = default)
        where TVault : MongoVault
    {
        var runner = Runner<TVault>();
        ThrowIfHistoriesShared();

        return runner.MigrateAsync(target, cancellationToken);
    }

    public Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault =>
        Runner<TVault>().GetVersionAsync(cancellationToken);

    private VaultMigrationRunner<TVault> Runner<TVault>() where TVault : MongoVault =>
        services.GetService<VaultMigrationRunner<TVault>>()
        ?? throw new InvalidOperationException($"{typeof(TVault).Name} isn't registered. Register it with AddMongoVault.");

    /// <summary>
    /// Vaults recording their migrations in one collection would each take the other's for their own, skipping and
    /// reverting what isn't theirs.
    /// </summary>
    private void ThrowIfHistoriesShared()
    {
        var shared = runners
            .Select(runner => (runner.VaultType, runner.History))
            .Where(runner => runner.History is not null)
            .GroupBy(runner => (runner.History!.Database.Client, runner.History.CollectionNamespace))
            .FirstOrDefault(group => group.Count() > 1);

        if (shared is not null)
        {
            throw new VaultConfigurationException(
                $"{string.Join(" and ", shared.Select(runner => runner.VaultType.Name))} record their migrations in the same " +
                $"collection, {shared.Key.CollectionNamespace}. Give each its own with Migrations(m => m.CollectionName(...)).");
        }
    }
}
