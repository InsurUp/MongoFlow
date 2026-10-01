using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Migrations;

/// <summary>
/// Indexes come from migrations, created through the vault's collections so their names live in one place. Building an
/// index on a collection that already has documents isn't allowed in a transaction, so this migration opts out.
/// </summary>
public sealed class CreatePolicyIndexes : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 0, 0);

    public bool UseTransaction => false;

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        var policy = Builders<Policy>.IndexKeys;

        await context.Vault.Policies.MongoCollection.Indexes.CreateManyAsync(context.Session,
            [
                // Lookups by key expect one document.
                new CreateIndexModel<Policy>(policy.Ascending(p => p.PolicyNumber), new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<Policy>(policy.Ascending(p => p.AgencyId).Ascending(p => p.Status).Descending(p => p.StartsAt)),
                new CreateIndexModel<Policy>(policy.Ascending(p => p.EndsAt), new CreateIndexOptions<Policy>
                {
                    PartialFilterExpression = Builders<Policy>.Filter.Eq(p => p.Status, PolicyStatus.Active)
                })
            ],
            cancellationToken);

        await context.Vault.Claims.MongoCollection.Indexes.CreateOneAsync(context.Session,
            new CreateIndexModel<Claim>(Builders<Claim>.IndexKeys.Ascending(c => c.PolicyNumber)),
            cancellationToken: cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
