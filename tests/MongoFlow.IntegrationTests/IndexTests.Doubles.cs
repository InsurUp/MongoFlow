using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Semver;

namespace MongoFlow.IntegrationTests;

// Orders of customers, with a status stored as text, an expiry and notes; and migrations that apply the declared indexes
// at a point of their own, in a transaction or not, and write after them.
public partial class IndexTests
{
    public enum OrderStatus
    {
        Open,
        Closed
    }

    public sealed class Order
    {
        public int Id { get; set; }

        public string Number { get; set; } = "";

        public string Customer { get; set; } = "";

        public DateTime PlacedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        [BsonRepresentation(BsonType.String)]
        public OrderStatus Status { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class StoreVault : MongoVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;
    }

    /// <summary>Recreates <c>Number_1</c>, which the vault declares unique, outside a transaction.</summary>
    public sealed class MakeNumbersUnique : IVaultMigration<StoreVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public bool UseTransaction => false;

        public Task UpAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            context.Collection(x => x.Orders).RecreateIndexAsync("Number_1", cancellationToken);

        public Task DownAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Applies the declared indexes, or those named, outside a transaction, so the migrations after it write under them.</summary>
    public sealed class IndexOrders : IVaultMigration<StoreVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public bool UseTransaction => false;

        public Task UpAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            context.Collection(x => x.Orders).ApplyIndexesAsync(["Number_1", "Retired_1"], cancellationToken);

        public Task DownAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Saves two orders with one number, which a unique index on the number refuses.</summary>
    public sealed class DuplicateNumbers : IVaultMigration<StoreVault>
    {
        public SemVersion Version { get; } = new(1, 1, 0);

        public async Task UpAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken)
        {
            context.Vault.Orders.AddRange([new Order { Id = 1, Number = "A-1" }, new Order { Id = 2, Number = "A-1" }]);
            await context.Vault.SaveAsync(cancellationToken);
        }

        public Task DownAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Applies the declared indexes in its transaction, which the server doesn't build indexes in.</summary>
    public sealed class IndexOrdersInATransaction : IVaultMigration<StoreVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public Task UpAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            context.Collection(x => x.Orders).ApplyIndexesAsync(cancellationToken);

        public Task DownAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Recreates <c>Retired_1</c>, which the vault no longer declares, so it's dropped.</summary>
    public sealed class RecreateRetired : IVaultMigration<StoreVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public bool UseTransaction => false;

        public Task UpAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            context.Collection(x => x.Orders).RecreateIndexAsync("Retired_1", cancellationToken);

        public Task DownAsync(MigrationContext<StoreVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
