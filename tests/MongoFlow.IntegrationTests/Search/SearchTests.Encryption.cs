using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Encryption;

namespace MongoFlow.IntegrationTests.Search;

public partial class SearchTests
{
    // A local master key, fixed so a failing test can be read again: 96 bytes, as the local KMS takes.
    private static readonly byte[] MasterKey = [.. Enumerable.Range(0, 96).Select(i => (byte)i)];

    [Test]
    public async Task SearchAsync_VaultThatEncrypts_SearchesItsPlainCollection()
    {
        // Arrange — notes searched through the client that encrypts the patients.
        await using var host = Clinic(notes => notes.SearchIndex("default", index => index.Map(x => x.Text, SearchField.String())));
        await host.SeedAsync("Notes", new Note { Id = 1, Text = "Fever since Monday" }, new Note { Id = 2, Text = "Checked, no fever" },
            new Note { Id = 3, Text = "Broken arm" });
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
        host.Vault.Patients.Add(new Patient { Id = 1, Name = "Ada", Ssn = "111-11" });
        await host.Vault.SaveAsync();

        // Act
        var search = await host.Vault.Notes.SearchAsync(Builders<Note>.Search.Text(x => x.Text, "fever"));

        // Assert
        await Verify(new
        {
            Found = (await search.Project(x => x.Text).ToListAsync()).Order(),
            Patient = await host.Vault.Patients.FirstOrDefaultAsync(x => x.Ssn == "111-11")
        });
    }

    [Test]
    public async Task MigrateAllAsync_CollectionWithSearchIndexesRebuiltEncrypted_WarnsTheyFollowedItsPlainCopy()
    {
        // Arrange — patients an earlier version stored in plain text, and searched.
        var database = Mongo.NewDatabase();
        var plain = database.GetCollection<BsonDocument>("Patients");
        await plain.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "Name", "Ada" }, { "Ssn", "111-11" } });
        await plain.SearchIndexes.CreateOrUpdateAsync("byName", BsonDocument.Parse("{ mappings: { dynamic: true } }"));
        await plain.SearchIndexes.WaitUntilQueryableAsync("byName");
        await using var host = Clinic(database: database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(_sink.Snapshot(MongoFlowLogEvents.Encryption.Category)
            .Where(entry => entry.EventId == MongoFlowLogEvents.Encryption.SearchIndexesFollowCopy));
    }

    private VaultHost<ClinicVault> Clinic(Action<IVaultCollectionBuilder<Note, int>>? notes = null,
        IMongoDatabase? database = null)
    {
        database ??= Mongo.NewDatabase();
        return new VaultHost<ClinicVault>(Mongo.Client, database, vault => vault
                .UseQueryableEncryption(qe => qe
                    .KeyVault(database.DatabaseNamespace.DatabaseName, "__keyVault")
                    .Kms(new LocalKms(MasterKey)))
                .Collection(x => x.Patients, patients => patients.Encrypt(x => x.Ssn, EncryptedQuery.Equality))
                .Collection(x => x.Notes, collection => notes?.Invoke(collection)),
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));
    }
}
