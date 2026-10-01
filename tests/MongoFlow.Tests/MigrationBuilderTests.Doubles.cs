using Semver;

namespace MongoFlow.Tests;

// A vault with migrations of every shape an assembly can hold: concrete, abstract, open generic, and another vault's.
public partial class MigrationBuilderTests
{
    public sealed class ArchiveVault : MongoVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;
    }

    /// <summary>A default configuration naming the collection migrations are recorded in.</summary>
    public sealed class HistoryDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public const string CollectionName = "default_history";

        public void Configure(IVaultBuilder<TVault> vault) => vault.Migrations(m => m.CollectionName(CollectionName));
    }

    public sealed class CreateArchive : ArchiveMigration
    {
        public override SemVersion Version { get; } = new(1, 0, 0);
    }

    public sealed class FillArchive : ArchiveMigration
    {
        public override SemVersion Version { get; } = new(1, 1, 0);
    }

    /// <summary>A base for the vault's migrations, which isn't one itself.</summary>
    public abstract class ArchiveMigration : IVaultMigration<ArchiveVault>
    {
        public abstract SemVersion Version { get; }

        public Task UpAsync(MigrationContext<ArchiveVault> context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DownAsync(MigrationContext<ArchiveVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An open generic migration, which can't be created without a type argument.</summary>
    public sealed class GenericMigration<T> : ArchiveMigration
    {
        public override SemVersion Version { get; } = new(9, 0, 0);
    }

    /// <summary>A migration of another vault, in the same assembly.</summary>
    public sealed class ShopMigration : IVaultMigration<ShopVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public Task UpAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DownAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
