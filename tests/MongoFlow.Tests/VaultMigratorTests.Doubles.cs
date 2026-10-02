using Semver;

namespace MongoFlow.Tests;

// A vault with two migrations of one version, and a migration of the shop vault.
public partial class VaultMigratorTests
{
    public sealed class JournalVault : MongoVault
    {
        public IVaultCollection<Order, int> Entries { get; init; } = null!;
    }

    public sealed class OpenJournal : JournalMigration;

    public sealed class ReopenJournal : JournalMigration;

    /// <summary>The journal's migrations, all of version 1.0.0.</summary>
    public abstract class JournalMigration : IVaultMigration<JournalVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public Task UpAsync(MigrationContext<JournalVault> context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DownAsync(MigrationContext<JournalVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SeedShop : IVaultMigration<ShopVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public Task UpAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DownAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
