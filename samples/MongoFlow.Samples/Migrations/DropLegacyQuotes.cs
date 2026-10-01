using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Migrations;

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
