using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests.Search;

// Products searched by name and embedding, of a tenant, soft deleted by a date; listings soft deleted by a flag, with
// tenants of value types.
public partial class SearchIndexBuilderTests
{
    public sealed class Product : ITenantOwned
    {
        public ObjectId Id { get; set; }

        public string Name { get; set; } = "";

        public string? TenantId { get; set; }

        public DateTime? DeletedAt { get; set; }

        public decimal Price { get; set; }

        public float[] Embedding { get; set; } = [];

        public Supplier? Supplier { get; set; }
    }

    public sealed class Supplier
    {
        [BsonElement("city")]
        public string City { get; set; } = "";
    }

    public sealed class Listing : ITenantOwned
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public string? TenantId { get; set; }

        public bool IsDeleted { get; set; }

        public ObjectId AgencyId { get; set; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid OwnerId { get; set; }
    }

    // A tenant as a document, which a search can't compare exactly.
    public sealed record Region(string Code);

    public sealed class Shipment
    {
        public int Id { get; set; }

        public Region? Region { get; set; }
    }

    public sealed class CatalogVault : MongoVault
    {
        public IVaultCollection<Product, ObjectId> Products { get; init; } = null!;

        public IVaultCollection<Shipment, int> Shipments { get; init; } = null!;

        public IVaultCollection<Listing, int> Listings { get; init; } = null!;
    }
}
