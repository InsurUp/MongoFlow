using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.Tests;

// A vault whose key can be null, to show writes rejecting a document without one; customers to join orders with; patients
// whose identity number is encrypted, which can't be searched.
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

    [BsonIgnoreExtraElements]
    public sealed class Patient
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Ssn { get; set; } = "";
    }

    public sealed class ClinicVault : MongoVault
    {
        public IVaultCollection<Patient, int> Patients { get; init; } = null!;
    }

    /// <summary>Stands in for MongoFlow.Encryption's encryption: reads and writes through the vault's client, unchanged.</summary>
    private sealed class PlainEncryption : VaultEncryption
    {
        public override IMongoDatabase Database(IMongoDatabase source) => source;

        public override ICollectionEncryption ForCollection<TDocument>(CollectionModel<TDocument> collection,
            IReadOnlyList<EncryptedMember> members) =>
            new PlainCollectionEncryption([.. members.Select(member => member.Path)]);

        public override Task PrepareAsync(VaultModel model,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Stands in for a collection's encryption: what it's handed comes back as it is.</summary>
    private sealed class PlainCollectionEncryption(IReadOnlyList<string> paths) : ICollectionEncryption
    {
        public IReadOnlyList<string> Paths { get; } = paths;

        public bool SingleDocumentWrites => false;

        public ValueTask EnsureReadyAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<BsonDocument> EncryptDocumentAsync(BsonDocument document,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(document);

        public ValueTask<BsonDocument> EncryptFilterAsync(BsonDocument filter,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(filter);

        public BsonDocument EncryptFilter(BsonDocument filter) => filter;

        public ValueTask<BsonValue> EncryptUpdateAsync(BsonValue update,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(update);
    }
}
