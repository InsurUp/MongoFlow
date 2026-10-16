using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests.Encryption;

public partial class QueryableEncryptionFeatureTests
{
    private static readonly byte[] MasterKey = new byte[96];

    public interface IFeatured
    {
        bool IsDeleted { get; set; }

        string? TenantId { get; set; }

        int Version { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Customer : IFeatured
    {
        public int Id { get; set; }

        public string Number { get; set; } = "";

        public string? Ssn { get; set; }

        public Address? Address { get; set; }

        public bool IsDeleted { get; set; }

        public string? TenantId { get; set; }

        public int Version { get; set; }
    }

    public sealed class Address
    {
        public string? Street { get; set; }
    }

    public sealed class Strict
    {
        public int Id { get; set; }

        public string? Ssn { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Keeping
    {
        public int Id { get; set; }

        public string? Ssn { get; set; }

        [BsonExtraElements]
        public BsonDocument? Extra { get; set; }
    }

    public sealed class CustomerVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;
    }

    public sealed class NumberedVault : MongoVault
    {
        public IVaultCollection<Customer, string> Customers { get; init; } = null!;
    }

    public sealed class StrictVault : MongoVault
    {
        public IVaultCollection<Strict, int> Stricts { get; init; } = null!;
    }

    public sealed class KeepingVault : MongoVault
    {
        public IVaultCollection<Keeping, int> Keepings { get; init; } = null!;
    }

    public sealed class RawVault : MongoVault
    {
        public IVaultCollection<BsonDocument> Raw { get; init; } = null!;
    }
}
