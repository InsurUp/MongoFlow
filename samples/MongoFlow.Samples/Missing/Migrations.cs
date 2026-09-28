#if MISSING_API
// Migrations registered per vault and created through DI, plus one runner for every vault.

using MongoDB.Driver;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Missing.PolicyMigrations;

public sealed class V1CreateCollections : IVaultMigration<PolicyVault>
{
    public int Version => 1;

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        await context.EnsureCollectionsAsync(cancellationToken);
        await context.EnsureIndexesAsync(cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Database.DropCollectionAsync("insurance_claims", cancellationToken);
}

public sealed class V2BackfillPolicyVersion(TimeProvider clock) : IVaultMigration<PolicyVault>
{
    public int Version => 2;

    public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Vault.Policies
            .Without(MultiTenancyFeature.Key)
            .Without(SoftDeleteFeature.Key)
            .MongoCollection
            .UpdateManyAsync(
                context.Session,
                p => p.Version == 0,
                Builders<Domain.Policy>.Update.Set(p => p.Version, 1),
                cancellationToken: cancellationToken);

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public static class MigrationStartup
{
    // Replaces core's static RegisteredVaults list: MongoFlow knows every registered vault.
    public static Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(cancellationToken);
}
#endif
