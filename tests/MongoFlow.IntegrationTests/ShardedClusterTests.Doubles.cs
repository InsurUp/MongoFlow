namespace MongoFlow.IntegrationTests;

// Orders of customers, numbered, with notes.
public partial class ShardedClusterTests
{
    public sealed class Order
    {
        public int Id { get; set; }

        public string Number { get; set; } = "";

        public string Customer { get; set; } = "";

        public string? Notes { get; set; }
    }

    public sealed class OrdersVault : MongoVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;
    }
}
