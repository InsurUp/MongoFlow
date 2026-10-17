using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests;

// Patients whose identity number and age are encrypted, by an encryption that marks what it would encrypt.
public partial class EncryptedQueriesTests
{
    [BsonIgnoreExtraElements]
    public sealed class Patient
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Ssn { get; set; }

        public int Age { get; set; }

        public bool Insured { get; set; }

        public Ward? Ward { get; set; }
    }

    public sealed class Ward
    {
        public string Name { get; set; } = "";
    }

    public sealed class Visit
    {
        public int Id { get; set; }

        public string PatientSsn { get; set; } = "";
    }

    public sealed class ClinicVault : MongoVault
    {
        public IVaultCollection<Patient, int> Patients { get; init; } = null!;

        public IVaultCollection<Visit, int> Visits { get; init; } = null!;
    }

    /// <summary>Stands in for MongoFlow.Encryption's encryption: reads and writes through the vault's client, unchanged.</summary>
    private sealed class MarkingEncryption : VaultEncryption
    {
        public override MongoDB.Driver.IMongoDatabase Database(MongoDB.Driver.IMongoDatabase source) => source;

        public override ICollectionEncryption ForCollection<TDocument>(CollectionModel<TDocument> collection,
            IReadOnlyList<EncryptedMember> members) =>
            new MarkingCollectionEncryption([.. members.Select(member => member.Path)]);

        public override Task PrepareAsync(VaultModel model,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task PrepareCollectionAsync(VaultModel model,
            ICollectionModel collection,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Stands in for a collection's encryption: a filter it encrypts shows as <c>{ encrypted: filter }</c>.</summary>
    private sealed class MarkingCollectionEncryption(IReadOnlyList<string> paths) : ICollectionEncryption
    {
        public IReadOnlyList<string> Paths { get; } = paths;

        public bool SingleDocumentWrites => false;

        public ValueTask EnsureReadyAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<BsonDocument> EncryptDocumentAsync(BsonDocument document,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(document);

        public ValueTask<BsonDocument> EncryptFilterAsync(BsonDocument filter,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(EncryptFilter(filter));

        public BsonDocument EncryptFilter(BsonDocument filter) => new("encrypted", filter);

        public ValueTask<BsonValue> EncryptUpdateAsync(BsonValue update,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(update);
    }
}
