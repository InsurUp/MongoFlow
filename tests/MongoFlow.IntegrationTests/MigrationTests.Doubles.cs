using MongoDB.Driver;
using Semver;

namespace MongoFlow.IntegrationTests;

// A registry vault and its migrations: through the vault, through the driver, outside a transaction, failing, leaving
// writes unsaved, and recording what they see; and vaults pinned to a version. Each notes its steps in a StepLog.
public partial class MigrationTests
{
    public sealed class Entry
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Status { get; set; }
    }

    public sealed class RegistryVault : MongoVault
    {
        public IVaultCollection<Entry, int> Entries { get; init; } = null!;
    }

    /// <summary>The steps migrations took, in order.</summary>
    public sealed class StepLog
    {
        public List<string> Entries { get; } = [];

        public void Add(string step) => Entries.Add(step);
    }

    /// <summary>Seeds two entries through the vault, whose save joins the migration's transaction.</summary>
    public sealed class SeedEntries(StepLog steps) : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public string Description => "Seed the registry";

        public async Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"up {Version}");
            context.Vault.Entries.AddRange([new Entry { Id = 1, Name = "first" }, new Entry { Id = 2, Name = "second" }]);
            await context.Vault.SaveAsync(cancellationToken);
        }

        public async Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"down {Version}");
            context.Vault.Entries.DeleteMany(x => x.Id <= 2);
            await context.Vault.SaveAsync(cancellationToken);
        }
    }

    /// <summary>Sets every entry's status with the driver, in the migration's session.</summary>
    public sealed class ActivateEntries(StepLog steps) : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(1, 1, 0);

        public Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"up {Version}");
            return SetStatus(context, "active", cancellationToken);
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"down {Version}");
            return SetStatus(context, null, cancellationToken);
        }

        private static Task SetStatus(MigrationContext<RegistryVault> context,
            string? status,
            CancellationToken cancellationToken) =>
            context.Vault.Entries.MongoCollection.UpdateManyAsync(context.Session, FilterDefinition<Entry>.Empty,
                Builders<Entry>.Update.Set(x => x.Status, status), cancellationToken: cancellationToken);
    }

    /// <summary>Indexes entries by name, which MongoDB doesn't allow in a transaction once they exist, so it opts out.</summary>
    public sealed class IndexNames(StepLog steps) : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(2, 0, 0);

        public bool UseTransaction => false;

        public Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"up {Version}");
            return context.Vault.Entries.MongoCollection.Indexes.CreateOneAsync(context.Session,
                new CreateIndexModel<Entry>(Builders<Entry>.IndexKeys.Ascending(x => x.Name)), cancellationToken: cancellationToken);
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"down {Version}");
            return context.Vault.Entries.MongoCollection.Indexes.DropOneAsync(context.Session, "Name_1", cancellationToken);
        }
    }

    /// <summary>Applies, but fails to revert.</summary>
    public sealed class Irreversible(StepLog steps) : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(2, 1, 0);

        public Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"up {Version}");
            return Task.CompletedTask;
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This migration can't be reverted.");
    }

    /// <summary>Saves an entry through the vault, then fails.</summary>
    public sealed class FailAfterSaving : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(3, 0, 0);

        public async Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            context.Vault.Entries.Add(new Entry { Id = 3, Name = "third" });
            await context.Vault.SaveAsync(cancellationToken);

            throw new InvalidOperationException("The migration failed after saving.");
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Queues an entry on the vault and returns without saving it.</summary>
    public sealed class ForgetToSave : IVaultMigration<RegistryVault>
    {
        public SemVersion Version { get; } = new(3, 0, 0);

        public Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            context.Vault.Entries.Add(new Entry { Id = 3, Name = "third" });
            return Task.CompletedTask;
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Records what its context holds, in a transaction.</summary>
    public sealed class ProbeInTransaction(StepLog steps,
        IVaultTransactionManager transactions) : ProbeContext(steps, transactions)
    {
        public override SemVersion Version { get; } = new(1, 0, 0);
    }

    /// <summary>Records what its context holds, outside a transaction.</summary>
    public sealed class ProbeOutsideTransaction(StepLog steps,
        IVaultTransactionManager transactions) : ProbeContext(steps, transactions)
    {
        public override SemVersion Version { get; } = new(1, 1, 0);

        public override bool UseTransaction => false;
    }

    /// <summary>
    /// Records whether its context's database is the vault's, and whether its session is in a transaction, the one the
    /// scope's vault saves would join. It's created from the migration's scope, so it gets that scope's transactions.
    /// </summary>
    public abstract class ProbeContext(StepLog steps,
        IVaultTransactionManager transactions) : IVaultMigration<RegistryVault>
    {
        public abstract SemVersion Version { get; }

        public virtual bool UseTransaction => true;

        public Task UpAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken)
        {
            var vaultsDatabase = context.Vault.Entries.MongoCollection.Database.DatabaseNamespace;
            steps.Add($"{Version}: the vault's database {context.Database.DatabaseNamespace == vaultsDatabase}, in a " +
                      $"transaction {context.Session.IsInTransaction}, the scope's {ReferenceEquals(transactions.Current?.Session, context.Session)}");

            return Task.CompletedTask;
        }

        public Task DownAsync(MigrationContext<RegistryVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Seeds an order through the shop vault.</summary>
    public sealed class SeedOrders(StepLog steps) : IVaultMigration<ShopVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public async Task UpAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"shop up {Version}");
            context.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
            await context.Vault.SaveAsync(cancellationToken);
        }

        public Task DownAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Pinned to 1.1.0, below its highest migration, 2.0.0.</summary>
    [MongoVersion("1.1.0")]
    public sealed class PinnedVault : MongoVault
    {
        public IVaultCollection<Entry, int> Entries { get; init; } = null!;
    }

    /// <summary>Pinned to 1.5.0, which none of its migrations has.</summary>
    [MongoVersion("1.5.0")]
    public sealed class MistypedVault : MongoVault
    {
        public IVaultCollection<Entry, int> Entries { get; init; } = null!;
    }

    public sealed class PinnedFirst(StepLog steps) : NotedMigration<PinnedVault>(steps, 1, 0);

    public sealed class PinnedSecond(StepLog steps) : NotedMigration<PinnedVault>(steps, 1, 1);

    public sealed class PinnedThird(StepLog steps) : NotedMigration<PinnedVault>(steps, 2, 0);

    public sealed class MistypedFirst(StepLog steps) : NotedMigration<MistypedVault>(steps, 1, 0);

    public sealed class MistypedSecond(StepLog steps) : NotedMigration<MistypedVault>(steps, 2, 0);

    /// <summary>A migration that only notes its steps.</summary>
    public abstract class NotedMigration<TVault>(StepLog steps,
        int major,
        int minor) : IVaultMigration<TVault> where TVault : MongoVault
    {
        public SemVersion Version { get; } = new(major, minor, 0);

        public Task UpAsync(MigrationContext<TVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"up {Version}");
            return Task.CompletedTask;
        }

        public Task DownAsync(MigrationContext<TVault> context, CancellationToken cancellationToken)
        {
            steps.Add($"down {Version}");
            return Task.CompletedTask;
        }
    }
}
