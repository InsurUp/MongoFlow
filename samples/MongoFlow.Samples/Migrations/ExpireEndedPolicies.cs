using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Migrations;

/// <summary>A data fix through the vault. Its saves join the migration's transaction.</summary>
public sealed class ExpireEndedPolicies(TimeProvider clock) : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 1, 0);

    public string Description => "Expire active policies whose end date has passed";

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // Every agency's policies, deleted ones included.
        context.Vault.Policies
            .Without(MultiTenancyFeature.Key)
            .Without(SoftDeleteFeature.Key)
            .UpdateMany(
                p => p.Status == PolicyStatus.Active && p.EndsAt < now,
                Builders<Policy>.Update.Set(p => p.Status, PolicyStatus.Expired));

        await context.Vault.SaveAsync(cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
