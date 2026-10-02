using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Migrations;

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
