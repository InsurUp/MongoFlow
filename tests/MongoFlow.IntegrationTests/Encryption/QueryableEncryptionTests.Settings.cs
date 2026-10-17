using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests.Encryption;

public partial class QueryableEncryptionTests
{
    [Test]
    public async Task MigrateAllAsync_EncryptedCollectionDeclaringSettingsAndIndexes_CreatesItWithThem()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await using var host = PlainHost(database, customers =>
        {
            Encrypted(customers);
            customers
                .Settings(settings => settings
                    .Collation(new Collation("tr", strength: CollationStrength.Secondary))
                    .Validator(x => x.Version >= 0)
                    .ChangeStreamPreAndPostImages())
                .Index(keys => keys.Ascending(x => x.Name));
        });

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Options = await PlainOptionsAsync(database),
            Indexes = (await (await database.GetCollection<BsonDocument>("Customers").Indexes.ListAsync()).ToListAsync())
                .Select(index => index["name"]),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Collections.Category)
                .Concat(_sink.Snapshot(MongoFlowLogEvents.Indexes.Category))
                .Where(entry => entry.EventId is MongoFlowLogEvents.Collections.CollectionUpToDate or MongoFlowLogEvents.Indexes.IndexCreated)
        });
    }

    [Test]
    public async Task MigrateAllAsync_RebuildingACollectionWithAValidator_CopiesItsDocumentsThenAppliesIt()
    {
        // Arrange — documents stored before the validator, which they don't pass.
        var database = Mongo.NewDatabase();
        await database.GetCollection<BsonDocument>("Customers").InsertManyAsync(
            Customers(2).Select(customer => customer.ToBsonDocument().Set("Version", -1)));
        await using var host = PlainHost(database, customers =>
        {
            Encrypted(customers);
            customers.Settings(settings => settings.Validator(x => x.Version >= 0));
        });

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Copied = await database.GetCollection<BsonDocument>("Customers").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),
            Options = await PlainOptionsAsync(database),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Collections.Category)
        });
    }

    [Test]
    public async Task CreateAsync_InAMigration_CreatesTheEncryptedCollectionAnewWithItsSettings()
    {
        // Arrange — the migration drops the customers the migrator created, and creates them anew.
        var database = Mongo.NewDatabase();
        await using var host = PlainHost(database, customers =>
        {
            Encrypted(customers);
            customers.Settings(settings => settings.Validator(x => x.Version >= 0));
        }, migrations => migrations.Add<RecreateCustomers>());

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new { Schema = await EncryptedFieldsAsync(database, "Customers"), Options = await PlainOptionsAsync(database) });
    }

    // The customers' options as the server lists them, but for the fields they're encrypted with.
    private static async Task<BsonDocument> PlainOptionsAsync(IMongoDatabase database)
    {
        var options = new ListCollectionsOptions { Filter = new BsonDocument("name", "Customers") };
        var stored = await (await database.ListCollectionsAsync(options)).SingleAsync();
        var plain = stored["options"].AsBsonDocument;
        plain.Remove("encryptedFields");
        return plain;
    }
}
