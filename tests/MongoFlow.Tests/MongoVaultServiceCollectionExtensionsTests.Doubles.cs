namespace MongoFlow.Tests;

// Vaults registered behind an app interface, or created with dependencies.
public partial class MongoVaultServiceCollectionExtensionsTests
{
    /// <summary>An app's interface for its vault.</summary>
    public interface IShopVault : IMongoVault
    {
        IVaultCollection<Order, int> Orders { get; }
    }

    public sealed class InterfacedVault : MongoVault, IShopVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;
    }

    /// <summary>The customer of the request: a scoped service.</summary>
    public sealed class CurrentCustomer;

    /// <summary>A vault that depends on a scoped service.</summary>
    public sealed class DependentVault(CurrentCustomer customer) : MongoVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public CurrentCustomer Customer => customer;
    }
}
