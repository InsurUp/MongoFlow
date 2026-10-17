using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.Tests.Indexes;

// Invoices of a tenant, with a status stored as text, an element named otherwise and a tax number that can be encrypted;
// a feature that indexes every collection's tenant; and defaults that declare an index and a collation of their own.
public partial class IndexBuilderTests
{
    public enum InvoiceStatus
    {
        Draft,
        Issued
    }

    [BsonIgnoreExtraElements]
    public sealed class Invoice : ITenantOwned
    {
        public int Id { get; set; }

        [BsonElement("number")]
        public string Number { get; set; } = "";

        public string Customer { get; set; } = "";

        public DateTime IssuedAt { get; set; }

        public DateTime? DueAt { get; set; }

        [BsonRepresentation(BsonType.String)]
        public InvoiceStatus Status { get; set; }

        public decimal Total { get; set; }

        public string? TenantId { get; set; }

        public string TaxNumber { get; set; } = "";

        public string? Notes { get; set; }

        public Address? Address { get; set; }
    }

    public sealed class Address
    {
        public string City { get; set; } = "";
    }

    public sealed class BillingVault : MongoVault
    {
        public IVaultCollection<Invoice, int> Invoices { get; init; } = null!;
    }

    /// <summary>A feature that indexes the tenant of every collection.</summary>
    public sealed class TenantIndexFeature : IVaultFeature, IVaultCollectionConfiguration
    {
        public static FeatureKey Key { get; } = new("tenant-index");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

        public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection) =>
            collection.Index(keys => keys.Ascending("TenantId"));
    }

    /// <summary>Defaults that index invoices by customer, named <c>by_customer</c>, and compare their strings in English.</summary>
    public sealed class InvoiceDefaults<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => vault.ForEachCollection(new InvoiceCollectionDefaults());
    }

    private sealed class InvoiceCollectionDefaults : IVaultCollectionConfiguration
    {
        public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
        {
            if (collection is IVaultCollectionBuilder<Invoice> invoices)
            {
                invoices
                    .Index(keys => keys.Ascending(x => x.Customer), index => index.Name("by_customer"))
                    .Settings(settings => settings.Collation(new Collation("en")).ChangeStreamPreAndPostImages());
            }
        }
    }
}
