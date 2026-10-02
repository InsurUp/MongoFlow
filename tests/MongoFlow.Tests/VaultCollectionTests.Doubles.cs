using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests;

// A vault whose key can be null, to show writes rejecting a document without one; customers to join orders with.
public partial class VaultCollectionTests
{
    /// <summary>A customer, whom orders name.</summary>
    public sealed class Customer : ISoftDeletable
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public bool IsDeleted { get; set; }
    }

    public sealed class SalesVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;

        public IVaultCollection<Order, int> Orders { get; init; } = null!;
    }

    /// <summary>Customers kept in another database than the <see cref="SalesVault"/>'s.</summary>
    public sealed class ArchiveVault : MongoVault
    {
        public const string Database = "archive";

        public IVaultCollection<Customer, int> Customers { get; init; } = null!;
    }

    public sealed class Product
    {
        public string? Id { get; set; }

        public string Name { get; set; } = "";
    }

    [BsonIgnoreExtraElements]
    public sealed class Note
    {
        public string Text { get; set; } = "";
    }

    /// <summary>Limits documents with a tenant to the first one.</summary>
    public sealed class TenantOneFeature : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("tenant-one");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
            vault.QueryFilter<ITenantOwned>(x => x.TenantId == "t-1");
    }

    public sealed class CatalogVault : MongoVault
    {
        public IVaultCollection<Product, string> Products { get; init; } = null!;

        public IVaultCollection<Note> Notes { get; init; } = null!;
    }
}
