using Microsoft.Extensions.DependencyInjection;
using Semver;

namespace MongoFlow;

internal sealed class VaultMigrator(IServiceProvider services,
    IEnumerable<IVaultMigrationRunner> runners) : IVaultMigrator
{
    public Task MigrateAllAsync(CancellationToken cancellationToken = default) =>
        MigrateAllAsync(new MigrateOptions(), cancellationToken);

    public async Task MigrateAllAsync(MigrateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfHistoriesShared();

        foreach (var runner in runners)
        {
            await runner.MigrateAsync(target: null, options, cancellationToken);
        }

        if (options.SyncIndexes)
        {
            await SyncIndexesAsync([.. runners], options, cancellationToken);
        }
    }

    public Task MigrateAsync<TVault>(SemVersion? target = null,
        CancellationToken cancellationToken = default)
        where TVault : MongoVault =>
        MigrateAsync<TVault>(target, new MigrateOptions(), cancellationToken);

    public async Task MigrateAsync<TVault>(SemVersion? target,
        MigrateOptions options,
        CancellationToken cancellationToken = default)
        where TVault : MongoVault
    {
        ArgumentNullException.ThrowIfNull(options);
        var runner = Runner<TVault>();
        ThrowIfHistoriesShared();

        await runner.MigrateAsync(target, options, cancellationToken);

        if (options.SyncIndexes)
        {
            await SyncIndexesAsync([runner], options, cancellationToken);
        }
    }

    public Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault =>
        Runner<TVault>().GetVersionAsync(cancellationToken);

    private VaultMigrationRunner<TVault> Runner<TVault>() where TVault : MongoVault =>
        services.GetService<VaultMigrationRunner<TVault>>()
        ?? throw new InvalidOperationException($"{typeof(TVault).Name} isn't registered. Register it with AddMongoVault.");

    // The collections the migrated vaults declare, with what every registered vault declares of each, so migrating one
    // vault drops no index another declares of a collection they share.
    private Task SyncIndexesAsync(IReadOnlyList<IVaultMigrationRunner> migrated,
        MigrateOptions options,
        CancellationToken cancellationToken) =>
        new VaultIndexSync(services.GetService<TimeProvider>() ?? TimeProvider.System)
            .RunAsync([.. migrated.Select(runner => runner.Model)], [.. runners.Select(runner => runner.Model)], options, cancellationToken);

    /// <summary>
    /// Vaults recording their migrations in one collection would each take the other's for their own, skipping and
    /// reverting what isn't theirs.
    /// </summary>
    private void ThrowIfHistoriesShared()
    {
        var shared = runners
            .Select(runner => (runner.VaultType, runner.History, runner.Client))
            .Where(runner => runner.History is not null)
            .GroupBy(runner => (runner.Client, runner.History!.CollectionNamespace))
            .FirstOrDefault(group => group.Count() > 1);

        if (shared is not null)
        {
            throw new VaultConfigurationException(
                $"{string.Join(" and ", shared.Select(runner => runner.VaultType.Name))} record their migrations in the same " +
                $"collection, {shared.Key.CollectionNamespace}. Give each its own with Migrations(m => m.CollectionName(...)).");
        }
    }
}
