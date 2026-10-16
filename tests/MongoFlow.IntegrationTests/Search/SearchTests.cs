using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow.IntegrationTests.Search;

/// <summary>
/// MongoDB Search on a vault's collections, against MongoDB Atlas Local (mongod 8.3, mongot 1.70):
/// <list type="number">
/// <item>searches and vector searches filter with the query filters in the index, a tenant and soft delete's flag among
/// them, so they find only what the query filters show; what the index can't filter on is matched after the search;</item>
/// <item>a feature switched off, a missing tenant and query filters that match nothing are searched as reads find them;</item>
/// <item><c>$searchMeta</c> counts only what the query filters show, and searches run outside an open transaction;</item>
/// <item>the query filters as clauses filter a pipeline built by hand as <c>SearchAsync</c> does, and an index the vault
/// doesn't declare is read from the server.</item>
/// </list>
/// </summary>
[Category("Search")]
public partial class SearchTests
{
    private static readonly SearchDefinition<Customer> Ada = Builders<Customer>.Search.Text(x => x.Name, "ada");

    private static readonly QueryVector Lovelace = new(new[] { 1f, 0f, 0f });

    private readonly LogSink _sink = new();

    [ClassDataSource<SearchFixture>(Shared = SharedType.PerTestSession)]
    public required SearchFixture Mongo { get; init; }

    [Test]
    public async Task SearchAsync_TenantAndSoftDelete_AreFilteredInTheIndex()
    {
        // Arrange
        await using var host = await CrmAsync();

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada);

        // Assert
        await Verify(new { Found = await Names(search), Pipeline = search.ToString() });
    }

    [Test]
    public async Task SearchAsync_QueryFilterTheIndexDoesntMap_IsMatchedAfterTheSearch()
    {
        // Arrange — every agency's customers, so the score alone tells them apart.
        await using var host = await CrmAsync(allAgencies: true, customers: customers => customers.QueryFilter(x => x.Score > 4));

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada);

        // Assert
        await Verify(new { Found = await Names(search), Logs = Logged(MongoFlowLogEvents.Search.FilterFieldNotPreFiltered) });
    }

    [Test]
    public async Task SearchAsync_WithoutSoftDelete_FindsTheDeleted()
    {
        // Arrange
        await using var host = await CrmAsync();

        // Act
        var search = await host.Vault.Customers.Without(SoftDeleteFeature.Key).SearchAsync(Ada);

        // Assert
        await Assert.That(await Names(search)).IsEquivalentTo(["Ada Byron", "Ada Lovelace"]);
    }

    [Test]
    public async Task SearchAsync_NoCurrentAgency_FindsThoseWithout()
    {
        // Arrange — one stored without an agency, another with a null one, as reads find both.
        await using var host = await CrmAsync(agency: null,
            extra: new BsonDocument { { "_id", 6 }, { "Name", "Ada Null" }, { "AgencyId", BsonNull.Value }, { "IsDeleted", false } });

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada);

        // Assert
        await Assert.That(await Names(search)).IsEquivalentTo(["Ada Noagency", "Ada Null"]);
    }

    [Test]
    public async Task SearchAsync_QueryFiltersMatchNothing_FindsNothing()
    {
        // Arrange
        await using var host = await CrmAsync(customers: customers => customers.QueryFilter(_ => _ => false));

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada);

        // Assert
        await Assert.That(await Names(search)).IsEmpty();
    }

    [Test]
    public async Task SearchAsync_InATransaction_SearchesOutsideIt()
    {
        // Arrange — the server doesn't search in a transaction.
        await using var host = await CrmAsync();
        await using var transaction = await host.Transactions.BeginAsync();

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada);

        // Assert
        await Assert.That(await Names(search)).IsEquivalentTo(["Ada Lovelace"]);
    }

    [Test]
    public async Task SearchMetaAsync_QueryFiltersInTheIndex_CountsWhatTheyShow()
    {
        // Arrange
        await using var host = await CrmAsync();
        var byAgency = Builders<Customer>.Search.Facet(Builders<Customer>.Search.Text(x => x.Name, "ada babbage"),
            Builders<Customer>.SearchFacet.String("agencies", x => x.AgencyId));

        // Act
        var meta = await host.Vault.Customers.SearchMetaAsync(byAgency, count: new SearchCountOptions { Type = SearchCountType.Total });

        // Assert
        await Verify(meta);
    }

    [Test]
    public async Task VectorSearchAsync_TenantAndSoftDelete_AreFilteredInTheIndex()
    {
        // Arrange
        await using var host = await CrmAsync();

        // Act
        var search = await host.Vault.Customers.VectorSearchAsync(x => x.Embedding, Lovelace, 10,
            new VectorSearchOptions<Customer> { IndexName = "vectors", NumberOfCandidates = 50 });

        // Assert
        await Verify(await search.Project(x => x.Name).ToListAsync());
    }

    [Test]
    public async Task VectorSearchAsync_QueryFilterNotAFilterField_IsMatchedAfterTheSearch()
    {
        // Arrange
        await using var host = await CrmAsync(customers: customers => customers.QueryFilter(x => x.Score > 2));

        // Act
        var search = await host.Vault.Customers.VectorSearchAsync(x => x.Embedding, Lovelace, 10,
            new VectorSearchOptions<Customer> { IndexName = "vectors", NumberOfCandidates = 50 });

        // Assert
        await Verify(await search.Project(x => x.Name).ToListAsync());
    }

    [Test]
    public async Task SearchFiltersAsync_InAPipelineBuiltByHand_FilterAsSearchAsyncDoes()
    {
        // Arrange
        await using var host = await CrmAsync(customers: customers => customers.QueryFilter(x => x.Score > 2));
        var filters = await host.Vault.Customers.SearchFiltersAsync();

        // Act
        var search = host.Vault.Customers.MongoCollection.Aggregate()
            .Search(filters.Apply(Builders<Customer>.Search.Text(x => x.Name, "ada babbage")))
            .Match(new BsonDocumentFilterDefinition<Customer>(filters.PostFilter!));

        // Assert
        await Assert.That(await Names(search)).IsEquivalentTo(["Ada Lovelace"]);
    }

    [Test]
    public async Task SearchAsync_IndexTheVaultDoesntDeclare_IsReadFromTheServer()
    {
        // Arrange — an index set up by a migration, which maps the agency but not soft delete's flag.
        await using var host = await CrmAsync(configure: vault => vault.Migrations(migrations => migrations.Add<IndexCustomersByHand>()));

        // Act
        var search = await host.Vault.Customers.SearchAsync(Ada, new SearchOptions<Customer> { IndexName = "byHand" });

        // Assert
        await Verify(new
        {
            Found = await Names(search),
            Pipeline = search.ToString(),
            Logs = Logged(MongoFlowLogEvents.Search.IndexListed, MongoFlowLogEvents.Search.FilterFieldNotPreFiltered)
        });
    }

    // Customers of agency a-1, or all agencies, soft deleted, searched by name and embedding, seeded, then migrated, so the
    // indexes are built over what's stored.
    private async Task<VaultHost<CrmVault>> CrmAsync(string? agency = "a-1",
        bool allAgencies = false,
        Action<IVaultCollectionBuilder<Customer, int>>? customers = null,
        Action<IVaultBuilder<CrmVault>>? configure = null,
        MigrateOptions? migrate = null,
        params BsonDocument[] extra)
    {
        var host = Mongo.Host<CrmVault>(vault =>
            {
                vault.UseSoftDelete((Customer x) => x.IsDeleted)
                    .UseMultiTenancy((Customer x) => x.AgencyId, _ => agency, _ => allAgencies)
                    .Collection(x => x.Customers, collection =>
                    {
                        collection
                            .SearchIndex("default", index => index.Map(x => x.Name, SearchField.String()))
                            .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine));
                        customers?.Invoke(collection);
                    });
                configure?.Invoke(vault);
            },
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));

        await host.SeedAsync("Customers", Customers());
        if (extra.Length > 0)
        {
            await host.SeedAsync("Customers", extra);
        }

        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(migrate ?? new MigrateOptions());
        return host;
    }

    // What the search logged of events, leaving out the migrator's, which set up indexes in parallel.
    private IReadOnlyList<LogEntry> Logged(params int[] events) =>
        [.. _sink.Snapshot(MongoFlowLogEvents.Search.Category).Where(entry => events.Contains(entry.EventId))];

    private static async Task<List<string>> Names(IAggregateFluent<Customer> search) =>
        [.. (await search.Project(x => x.Name).ToListAsync()).Order()];
}
