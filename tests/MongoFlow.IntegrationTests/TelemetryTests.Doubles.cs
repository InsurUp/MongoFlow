using Semver;

namespace MongoFlow.IntegrationTests;

// A vault with a concurrency token, for a save that conflicts, and migrations of the shop vault: one that saves through
// the vault and reverts, and one that fails.
public partial class TelemetryTests
{
    public sealed class Ticket
    {
        public int Id { get; set; }

        public int Version { get; set; }
    }

    public sealed class TicketVault : MongoVault
    {
        public IVaultCollection<Ticket, int> Tickets { get; init; } = null!;
    }

    /// <summary>Adds an order through the vault, whose save joins the migration's transaction, and deletes it to revert.</summary>
    public sealed class AddOrder : IVaultMigration<ShopVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public async Task UpAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken)
        {
            context.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
            await context.Vault.SaveAsync(cancellationToken);
        }

        public async Task DownAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken)
        {
            context.Vault.Orders.DeleteByKey(1);
            await context.Vault.SaveAsync(cancellationToken);
        }
    }

    /// <summary>Fails to apply.</summary>
    public sealed class FailToApply : IVaultMigration<ShopVault>
    {
        public SemVersion Version { get; } = new(2, 0, 0);

        public Task UpAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The migration failed.");

        public Task DownAsync(MigrationContext<ShopVault> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
