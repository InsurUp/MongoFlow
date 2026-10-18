using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow.IntegrationTests.Search;

public partial class SearchTests
{
    private static readonly MigrateOptions Sync = new() { SyncIndexes = true };

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
    public async Task MigrateAllAsync_TextWithAnAlternateAsDeclared_LeavesItForSearchesToPick()
    {
        // Arrange — names indexed whole too, as one token, for a wildcard to match across the space in a name.
        await using var host = await CrmAsync(customers: customers => customers.SearchIndex("default", index => index
            .Map(x => x.Name, SearchField.String().Multi("keyword", SearchField.String("lucene.keyword")))));
        var before = _sink.Snapshot(MongoFlowLogEvents.Search.Category).Count;

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert — each server lists the alternate its own way.
        var search = await host.Vault.Customers.SearchAsync(Builders<Customer>.Search.Wildcard(
            Builders<Customer>.SearchPath.Analyzer(x => x.Name, "keyword"), "Ada L*", allowAnalyzedField: true));
        await Verify(new
        {
            Index = (await IndexesAsync(host.Database)).Single(index => index.Name == "default").Definition,
            Logs = Migrated(before),
            Found = await Names(search)
        });
    }

    [Test]
    public async Task MigrateAllAsync_DefinitionDeclaredAsMapsWithStaticDocuments_LeavesTheIndex()
    {
        // Arrange — the index written by hand, dynamic, with documents that carry no dynamic, so they're static.
        await using var first = await CrmAsync(customers: customers => customers.SearchIndex("default", index => index
            .Definition(BsonDocument.Parse("""
                {
                    mappings: {
                        dynamic: true,
                        fields: {
                            Address: {
                                type: 'document',
                                fields: { City: { type: 'token' }, Country: { type: 'document', fields: { Code: { type: 'token' } } } }
                            }
                        }
                    }
                }
                """))));
        await using var declared = new VaultHost<CrmVault>(_mongo.Client, first.Database, vault => vault
                .UseSoftDelete((Customer x) => x.IsDeleted)
                .UseMultiTenancy((Customer x) => x.AgencyId, _ => "a-1")
                .Collection(x => x.Customers, customers => customers
                    .SearchIndex("default", index => index
                        .Dynamic()
                        .DocumentsDynamic(false)
                        .Map(x => x.Name, SearchField.String())
                        .Map("Address.City", SearchField.Token())
                        .Map("Address.Country.Code", SearchField.Token()))
                    .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine))),
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));
        var before = _sink.Snapshot(MongoFlowLogEvents.Search.Category).Count;

        // Act
        await declared.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert — each server lists the documents its own way.
        await Verify(new
        {
            Index = (await IndexesAsync(first.Database)).Single(index => index.Name == "default").Definition,
            Logs = Migrated(before)
        });
    }

    [Test]
    public async Task MigrateAllAsync_DynamicIndexWithoutTheFlagMapped_LeavesItForSearchesToFilterOnTheFlag()
    {
        // Act — a migration sets the index up by hand, as it was before soft delete; the vault declares it dynamic.
        await using var host = await CrmAsync(customers: customers => customers.SearchIndex("default", index => index.Dynamic()),
            configure: vault => vault.Migrations(migrations => migrations.Add<IndexCustomersDynamicallyByHand>()));

        // Assert — the deleted Ada Byron is left out in the index, by the flag it indexes dynamically, so it counts too.
        var search = await host.Vault.Customers.SearchAsync(Ada);
        var meta = await host.Vault.Customers.SearchMetaAsync(Ada, count: new SearchCountOptions { Type = SearchCountType.Total });
        await Verify(new
        {
            Logs = Migrated().Where(entry => entry.Message.Contains(" default ")),
            Found = await Names(search),
            Pipeline = search.ToString(),
            meta.Count
        }).IgnoreParametersForVerified();
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

    [Test]
    public async Task MigrateAllAsync_SyncingSearchIndexesAsDeclared_ChangesNothing()
    {
        // Arrange
        await using var host = await CrmAsync(migrate: Sync);
        var before = _sink.Snapshot(MongoFlowLogEvents.Search.Category).Count;

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(Sync);

        // Assert
        await Verify(Migrated(before)).IgnoreParametersForVerified();
    }

    [Test]
    public async Task MigrateAllAsync_SyncingASearchIndexNoVaultDeclares_DropsIt()
    {
        // Act — a migration sets one up by hand.
        await using var host = await CrmAsync(configure: vault => vault.Migrations(migrations => migrations.Add<IndexCustomersByHand>()),
            migrate: Sync);

        // Assert — each server lists the definition dropped its own way.
        await Verify(new
        {
            Indexes = (await IndexesAsync(host.Database)).Select(index => index.Name),
            Logs = Migrated().Where(entry => entry.EventId == MongoFlowLogEvents.Search.IndexDropped)
        });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingASearchIndexDeclaredOfTheOtherType_CreatesItAnew()
    {
        // Act — a migration sets the vectors up by hand as a search index.
        await using var host = await CrmAsync(configure: vault => vault.Migrations(migrations => migrations.Add<IndexVectorsAsText>()),
            migrate: Sync);

        // Assert — each server lists the definition dropped its own way; the vectors are searched as declared.
        var search = await host.Vault.Customers.VectorSearchAsync(x => x.Embedding, Lovelace, 10,
            new VectorSearchOptions<Customer> { IndexName = "vectors", NumberOfCandidates = 50 });
        await Verify(new
        {
            Indexes = (await IndexesAsync(host.Database)).Select(index => new { index.Name, index.Type }),
            Logs = Migrated().Where(entry => entry.Message.Contains("vectors")),
            Found = await search.Project(x => x.Name).ToListAsync()
        });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingASearchIndexTwoVaultsDeclareOtherwise_ThrowsChangingNothing()
    {
        // Arrange — an analytics vault of the same customers declares the vectors as the CRM does, and the names otherwise.
        var database = _mongo.NewDatabase();
        await using var host = new VaultHost<CrmVault>(_mongo.Client,
            database,
            vault => vault.Collection(x => x.Customers, customers => customers
                .SearchIndex("default", index => index.Map(x => x.Name, SearchField.String()))
                .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine))),
            services => services.AddMongoVault<AnalyticsVault>(vault => vault.UseDatabase(database).Collection(x => x.Customers, customers => customers
                .SearchIndex("default", index => index.Map(x => x.Name, SearchField.Token()))
                .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine)))));

        // Act
        var exception = await Assert.That(() => host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(Sync))
            .ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Message = Scrubbed(exception!.Message), Created = await database.ListCollectionNames().ToListAsync() })
            .DontIgnoreEmptyCollections()
            .IgnoreParametersForVerified();
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

    private static string Scrubbed(string message) => Database().Replace(message, "{database}");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();
}
