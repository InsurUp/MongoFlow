using MongoDB.Bson;
using MongoDB.Driver;
using Semver;

namespace MongoFlow.IntegrationTests;

// Payments of a payer, by amount; and migrations that write to them, drop them, or drop them and create them anew.
public partial class CollectionSettingsTests
{
    public sealed class Payment
    {
        public int Id { get; set; }

        public string Payer { get; set; } = "";

        public decimal Amount { get; set; }
    }

    public sealed class LedgerVault : MongoVault
    {
        public IVaultCollection<Payment, int> Payments { get; init; } = null!;
    }

    /// <summary>Saves a payment of a negative amount, which a validator of positive amounts refuses.</summary>
    public sealed class SaveARefund : IVaultMigration<LedgerVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public async Task UpAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken)
        {
            context.Vault.Payments.Add(new Payment { Id = 1, Payer = "Ada", Amount = -5 });
            await context.Vault.SaveAsync(cancellationToken);
        }

        public Task DownAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Drops the payments, which the migrator created with their settings.</summary>
    public sealed class DropPayments : IVaultMigration<LedgerVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public bool UseTransaction => false;

        public Task UpAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken) =>
            context.Database.DropCollectionAsync("Payments", cancellationToken);

        public Task DownAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Drops the payments and creates them anew, with their settings, as a migration rebuilding them would.</summary>
    public sealed class RecreatePayments : IVaultMigration<LedgerVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public bool UseTransaction => false;

        public async Task UpAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken)
        {
            await context.Database.DropCollectionAsync("Payments", cancellationToken);
            await context.Collection(x => x.Payments).CreateAsync(cancellationToken);
            await context.Database.GetCollection<BsonDocument>("Payments").InsertOneAsync(
                new BsonDocument { { "_id", 1 }, { "Payer", "Ada" }, { "Amount", 5m } }, cancellationToken: cancellationToken);
        }

        public Task DownAsync(MigrationContext<LedgerVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
