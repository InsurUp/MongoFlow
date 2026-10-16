using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoFlow.Encryption;

namespace MongoFlow.IntegrationTests.Encryption;

// Customers whose identity number, salary, notes and street are encrypted, behind soft delete and a concurrency token.
public partial class QueryableEncryptionTests
{
    // A local master key, fixed so a failing test can be read again: 96 bytes, as the local KMS takes.
    private static readonly byte[] MasterKey = [.. Enumerable.Range(0, 96).Select(i => (byte)i)];

    public interface IVersioned
    {
        int Version { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Customer : IVersioned
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Ssn { get; set; }

        public int Salary { get; set; }

        public string? Notes { get; set; }

        public Address? Address { get; set; }

        public bool IsDeleted { get; set; }

        public int Version { get; set; }
    }

    public sealed class Address
    {
        public string City { get; set; } = "";

        public string? Street { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Ticket
    {
        public ObjectId Id { get; set; }

        public string? Secret { get; set; }
    }

    public sealed class TicketVault : MongoVault
    {
        public IVaultCollection<Ticket, ObjectId> Tickets { get; init; } = null!;
    }

    [BsonIgnoreExtraElements]
    public sealed class Supplier
    {
        public int Id { get; set; }

        public string? TaxNumber { get; set; }
    }

    public sealed class CustomerVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;
    }

    public sealed class SupplierVault : MongoVault
    {
        public IVaultCollection<Supplier, int> Suppliers { get; init; } = null!;
    }

    /// <summary>A vault that stores nothing encrypted, for the save that joins an encrypted one's transaction.</summary>
    public sealed class LedgerVault : MongoVault
    {
        public IVaultCollection<Entry, int> Entries { get; init; } = null!;
    }

    public sealed class Entry
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    /// <summary>The customers' encryption, as a vault declares it: Ssn for equality, Salary for ranges, Notes and Street just encrypted.</summary>
    private static void Encrypted(IVaultCollectionBuilder<Customer, int> customers) =>
        customers
            .Encrypt(x => x.Ssn, EncryptedQuery.Equality)
            .Encrypt(x => x.Salary, EncryptedQuery.Range(0, 1_000_000))
            .Encrypt(x => x.Notes)
            .Encrypt(x => x.Address!.Street);

    private static void UseEncryption<TVault>(IVaultBuilder<TVault> vault,
        string keyVaultDatabase) where TVault : MongoVault =>
        vault.UseQueryableEncryption(qe => qe
            .KeyVault(keyVaultDatabase, "__keyVault")
            .Kms(new LocalKms(MasterKey)));
}
