using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Migrations;

/// <summary>
/// The indexes <see cref="PolicyVault"/> declares, applied here rather than after the migrations, as the migrator would, so
/// the migrations after this one run on them. Building an index isn't allowed in a transaction, so this migration opts out.
/// </summary>
public sealed class CreatePolicyIndexes : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 0, 0);

    public bool UseTransaction => false;

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        await context.Collection(x => x.Policies).ApplyIndexesAsync(cancellationToken);
        await context.Collection(x => x.Claims).ApplyIndexesAsync(cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
