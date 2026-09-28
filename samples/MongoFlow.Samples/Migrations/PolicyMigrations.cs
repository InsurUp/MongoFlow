using MongoDB.Bson;
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

/// <summary>A schema change with the driver, in the migration's session.</summary>
public sealed class RenamePremiumField : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 2, 0);

    public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Rename(context, from: "premium", to: nameof(Policy.Premium), cancellationToken);

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Rename(context, from: nameof(Policy.Premium), to: "premium", cancellationToken);

    private static Task Rename(MigrationContext<PolicyVault> context, string from, string to, CancellationToken cancellationToken) =>
        context.Database.GetCollection<BsonDocument>("policies").UpdateManyAsync(
            context.Session,
            Builders<BsonDocument>.Filter.Exists(from),
            Builders<BsonDocument>.Update.Rename(from, to),
            cancellationToken: cancellationToken);
}

/// <summary>Dropping a collection isn't allowed in a transaction, so this migration opts out.</summary>
public sealed class DropLegacyQuotes : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(2, 0, 0);

    public bool UseTransaction => false;

    public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Database.DropCollectionAsync(context.Session, "legacy_quotes", cancellationToken);

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
