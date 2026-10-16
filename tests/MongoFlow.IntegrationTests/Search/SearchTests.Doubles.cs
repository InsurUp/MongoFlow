using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Semver;

namespace MongoFlow.IntegrationTests.Search;

// Customers of agencies, soft deleted, searched by name and by an embedding; patients whose identity number is
// encrypted, beside plain notes searched by text; a migration that sets up an index by hand; a search index as the server
// lists it.
public partial class SearchTests
{
    public sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        [BsonIgnoreIfNull]
        public string? AgencyId { get; set; }

        public bool IsDeleted { get; set; }

        public int Score { get; set; }

        public float[] Embedding { get; set; } = [];
    }

    public sealed class CrmVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;
    }

    [BsonIgnoreExtraElements]
    public sealed class Patient
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Ssn { get; set; }
    }

    public sealed class Note
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class ClinicVault : MongoVault
    {
        public IVaultCollection<Patient, int> Patients { get; init; } = null!;

        public IVaultCollection<Note, int> Notes { get; init; } = null!;
    }

    /// <summary>Sets up a search index of customers by hand, as a migration does for an index the vault doesn't declare.</summary>
    public sealed class IndexCustomersByHand : IVaultMigration<CrmVault>
    {
        public SemVersion Version { get; } = new(1, 0, 0);

        public string Description => "Index customers by name";

        public bool UseTransaction => false;

        public async Task UpAsync(MigrationContext<CrmVault> context,
            CancellationToken cancellationToken)
        {
            var indexes = context.Database.GetCollection<BsonDocument>("Customers").SearchIndexes;
            await indexes.CreateOrUpdateAsync("byHand",
                BsonDocument.Parse("{ mappings: { dynamic: false, fields: { Name: { type: 'string' }, AgencyId: { type: 'token' } } } }"),
                cancellationToken: cancellationToken);
            await indexes.WaitUntilQueryableAsync("byHand", cancellationToken: cancellationToken);
        }

        public Task DownAsync(MigrationContext<CrmVault> context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>A search index as the server lists it: its name, type, status, and the definition it's built to.</summary>
    public sealed record ListedIndex(string Name,
        string Type,
        string Status,
        bool Queryable,
        BsonValue Definition);

    // Five customers named Ada, of two agencies, one, or none, one deleted; and Bob, who isn't.
    private static Customer[] Customers() =>
    [
        new() { Id = 1, Name = "Ada Lovelace", AgencyId = "a-1", Score = 5, Embedding = [1f, 0f, 0f] },
        new() { Id = 2, Name = "Ada Byron", AgencyId = "a-1", IsDeleted = true, Score = 7, Embedding = [0.9f, 0.1f, 0f] },
        new() { Id = 3, Name = "Ada King", AgencyId = "a-2", Score = 9, Embedding = [0.8f, 0.2f, 0f] },
        new() { Id = 4, Name = "Ada Noagency", Score = 3, Embedding = [0.7f, 0.3f, 0f] },
        new() { Id = 5, Name = "Bob Babbage", AgencyId = "a-1", Score = 1, Embedding = [0f, 1f, 0f] }
    ];
}
