using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests.Search;

public partial class SearchTests
{
    [Test]
    public async Task MigrateAllAsync_IndexesDeclared_CreatesThemAndWaitsUntilQueryable()
    {
        // Arrange
        await using var host = await CrmAsync();

        // Assert — CrmAsync migrated; searching right after finds what's stored. Each server lists the indexes its own way.
        await Verify(new
        {
            Indexes = await IndexesAsync(host.Database),
            Logs = Migrated(),
            Found = await Names(await host.Vault.Customers.SearchAsync(Ada))
        });
    }

    [Test]
    public async Task MigrateAllAsync_NotWaitingForSearchIndexes_ReturnsOnceTheyAreCreated()
    {
        // Act — CrmAsync migrates with the options.
        await using var host = await CrmAsync(migrate: new MigrateOptions { WaitForSearchIndexes = false });

        // Assert — logged created, and neither building nor queryable; how far each has got is up to the server. Each
        // server lists the indexes its own way.
        await Verify(new
        {
            Indexes = (await IndexesAsync(host.Database)).Select(index => new { index.Name, index.Type }),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Search.Category).OrderBy(entry => entry.Message.Contains("vectorSearch"))
        });
    }

    [Test]
    public async Task MigrateAllAsync_IndexesAsDeclared_LeavesThem()
    {
        // Arrange
        await using var host = await CrmAsync();
        var before = _sink.Snapshot(MongoFlowLogEvents.Search.Category).Count;

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(Migrated(before)).IgnoreParametersForVerified();
    }

    [Test]
    public async Task MigrateAllAsync_DeclarationChanged_UpdatesTheIndex()
    {
        // Arrange — the same database, declared again with names mapped for autocomplete too.
        await using var first = await CrmAsync();
        await using var changed = new VaultHost<CrmVault>(_mongo.Client, first.Database, vault => vault
                .UseSoftDelete((Customer x) => x.IsDeleted)
                .UseMultiTenancy((Customer x) => x.AgencyId, _ => "a-1")
                .Collection(x => x.Customers, customers => customers
                    .SearchIndex("default", index => index.Map(x => x.Name, SearchField.String(), SearchField.Autocomplete()))
                    .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine))),
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));
        var before = _sink.Snapshot(MongoFlowLogEvents.Search.Category).Count;

        // Act
        await changed.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert — each server lists the indexes its own way.
        await Verify(new
        {
            Indexes = await IndexesAsync(first.Database),
            Logs = Migrated(before),
            Found = await Names(await changed.Vault.Customers.SearchAsync(
                Builders<Customer>.Search.Autocomplete(x => x.Name, "lov")))
        });
    }

    [Test]
    public async Task MigrateAllAsync_IndexesDeclared_TracesEachUnderTheMigration()
    {
        // Arrange
        using var spans = new ActivitySink();

        // Act
        await using var host = await CrmAsync();

        // Assert
        await Verify(spans.Snapshot().OrderBy(span => span.Name).ThenBy(span => span.Tags.GetValueOrDefault(MongoFlowTelemetry.Tags.IndexName))).IgnoreParametersForVerified();
    }

    // What the migrator logged of the indexes, after the first entries it logged before, in order of index, as it sets them
    // up in parallel; the building it waits for, at a status that depends on how quickly the server gets to it, left out.
    private IEnumerable<LogEntry> Migrated(int before = 0) =>
        _sink.Snapshot(MongoFlowLogEvents.Search.Category)
            .Skip(before)
            .Where(entry => entry.EventId != MongoFlowLogEvents.Search.IndexBuilding)
            .OrderBy(entry => entry.Message.Contains("vectorSearch"))
            .ThenBy(entry => entry.EventId);

    private static async Task<IEnumerable<ListedIndex>> IndexesAsync(IMongoDatabase database)
    {
        var listed = await (await database.GetCollection<BsonDocument>("Customers").SearchIndexes.ListAsync()).ToListAsync();
        return listed.OrderBy(index => index["name"].AsString).Select(index => new ListedIndex(
            index["name"].AsString,
            index.GetValue("type", BsonNull.Value) is BsonString type ? type.Value : null,
            index["status"].AsString,
            index["queryable"].AsBoolean,
            index["latestDefinition"]));
    }
}
