using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow.Tests;

public partial class VaultCollectionTests
{
    private static readonly SearchDefinition<Order> TextSearch = Builders<Order>.Search.Text(x => x.Customer, "ada");

    private static readonly BsonDocument VectorDefinition =
        BsonDocument.Parse("{ fields: [{ type: 'vector', path: 'Embedding', numDimensions: 3, similarity: 'cosine' }] }");

    private static readonly QueryVector Vector = new(new[] { 0.1f, 0.2f, 0.3f });

    [Test]
    public async Task SearchAsync_QueryFiltersTheIndexMaps_FiltersInTheSearch()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.SearchIndex("default", index => index.Map(x => x.Customer, SearchField.String())));

        // Act
        var search = await host.Vault.Orders.SearchAsync(TextSearch,
            new SearchOptions<Order> { Sort = Builders<Order>.Sort.Descending(x => x.Total), CountOptions = new SearchCountOptions() });

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task SearchAsync_QueryFilterTheIndexDoesntMap_MatchesItAfterTheSearchAndWarns()
    {
        // Arrange — a decimal is stored as Decimal128, which a search can't compare.
        var sink = new LogSink();
        await using var host = SearchHost(orders => orders
                .QueryFilter(x => x.Total > 10)
                .SearchIndex("default", index => index.Dynamic()),
            sink);

        // Act
        var first = await host.Vault.Orders.SearchAsync(TextSearch);
        await host.Vault.Orders.SearchAsync(TextSearch);

        // Assert
        await Verify(new { Pipeline = first.ToString(), Log = sink.Snapshot(MongoFlowLogEvents.Search.Category) });
    }

    [Test]
    public async Task SearchAsync_NoQueryFilters_IsTheSearchAlone()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .Collection(x => x.Orders, orders => orders.SearchIndex("search", index => index.Dynamic())));

        // Act
        var search = await host.Vault.Orders.SearchAsync(TextSearch, new SearchOptions<Order> { IndexName = "search" });

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task SearchAsync_QueryFiltersMatchNothing_MatchesNothingAfterTheSearch()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.QueryFilter(_ => _ => false));

        // Act
        var search = await host.Vault.Orders.SearchAsync(TextSearch);

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task SearchAsync_FacetOfWhatTheIndexFilters_FiltersInItsOperator()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.SearchIndex("default", index => index.Dynamic()));
        var facet = Builders<Order>.Search.Facet(TextSearch, Builders<Order>.SearchFacet.String("customers", x => x.Customer));

        // Act
        var search = await host.Vault.Orders.SearchAsync(facet);

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    [Arguments("count")]
    [Arguments("stored source")]
    [Arguments("facet")]
    public async Task SearchAsync_CountingWhatsMatchedAfterIt_ThrowsNotSupportedException(string counting)
    {
        // Arrange
        await using var host = SearchHost(orders => orders
            .QueryFilter(x => x.Total > 10)
            .SearchIndex("default", index => index.Dynamic()));
        var (search, options) = counting switch
        {
            "count" => (TextSearch, new SearchOptions<Order> { CountOptions = new SearchCountOptions { Type = SearchCountType.Total } }),
            "stored source" => (TextSearch, new SearchOptions<Order> { ReturnStoredSource = true }),
            _ => (Builders<Order>.Search.Facet(TextSearch, Builders<Order>.SearchFacet.String("customers", x => x.Customer)), null)
        };

        // Act & Assert
        await Assert.That(() => (Task)host.Vault.Orders.SearchAsync(search, options).AsTask()).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task SearchAsync_VectorSearchIndex_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.VectorSearchIndex("vectors", index => index.Definition(VectorDefinition)));

        // Act & Assert
        await Assert.That(() => (Task)host.Vault.Orders.SearchAsync(TextSearch, new SearchOptions<Order> { IndexName = "vectors" }).AsTask())
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments("lower bound")]
    [Arguments("total")]
    [Arguments("facets")]
    public async Task SearchMetaAsync_QueryFiltersMatchNothing_CountsNothing(string counting)
    {
        // Arrange — counted without the server, which the offline client doesn't reach.
        await using var host = SearchHost(orders => orders.QueryFilter(_ => _ => false));
        var search = counting == "facets"
            ? Builders<Order>.Search.Facet(TextSearch, Builders<Order>.SearchFacet.String("customers", x => x.Customer))
            : TextSearch;
        var count = counting == "total" ? new SearchCountOptions { Type = SearchCountType.Total } : null;

        // Act
        var meta = await host.Vault.Orders.SearchMetaAsync(search, count: count);

        // Assert
        await Verify(meta).UseParameters(counting);
    }

    [Test]
    public async Task SearchMetaAsync_QueryFilterTheIndexDoesntMap_ThrowsNotSupportedException()
    {
        // Arrange
        await using var host = SearchHost(orders => orders
            .QueryFilter(x => x.Total > 10)
            .SearchIndex("default", index => index.Dynamic()));

        // Act & Assert
        await ThrowsTask(() => host.Vault.Orders.SearchMetaAsync(TextSearch).AsTask()).IgnoreStackTrace();
    }

    [Test]
    public async Task VectorSearchAsync_QueryFilters_FilterInTheSearchWithItsOwn()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.VectorSearchIndex("vectors", index => index.Definition(VectorDefinition)));

        // Act
        var search = await host.Vault.Orders.VectorSearchAsync("Embedding", Vector, 10,
            new VectorSearchOptions<Order>
            {
                IndexName = "vectors",
                NumberOfCandidates = 100,
                Filter = Builders<Order>.Filter.Eq(x => x.TenantId, "t-2")
            });

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task VectorSearchAsync_ByMemberWithoutOptions_SearchesTheDefaultIndex()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.VectorSearchIndex("default", index => index.Definition(VectorDefinition)));

        // Act
        var search = await host.Vault.Orders.VectorSearchAsync(x => x.Customer, Vector, 10);

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task VectorSearchAsync_QueryFilterNotAFilterField_MatchesItAfterTheSearch()
    {
        // Arrange
        await using var host = SearchHost(orders => orders
            .QueryFilter(x => x.Customer != "bob")
            .VectorSearchIndex("default", index => index.Definition(VectorDefinition)));

        // Act
        var search = await host.Vault.Orders.VectorSearchAsync("Embedding", Vector, 10);

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task VectorSearchAsync_QueryFiltersMatchNothing_MatchesNothingAfterTheSearch()
    {
        // Arrange
        await using var host = SearchHost(orders => orders.QueryFilter(_ => _ => false));

        // Act
        var search = await host.Vault.Orders.VectorSearchAsync("Embedding", Vector, 10);

        // Assert
        await Verify(search.ToString());
    }

    [Test]
    public async Task VectorSearchAsync_StoredSourceWithWhatsMatchedAfterIt_ThrowsNotSupportedException()
    {
        // Arrange
        await using var host = SearchHost(orders => orders
            .QueryFilter(x => x.Customer != "bob")
            .VectorSearchIndex("default", index => index.Definition(VectorDefinition)));

        // Act & Assert
        await Assert.That(() => (Task)host.Vault.Orders.VectorSearchAsync("Embedding", Vector, 10, new VectorSearchOptions<Order> { ReturnStoredSource = true }).AsTask())
            .ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task SearchFiltersAsync_QueryFilters_AreTheClausesAndWhatsLeft()
    {
        // Arrange
        await using var host = SearchHost(orders => orders
            .QueryFilter(x => x.Total > 10)
            .SearchIndex("default", index => index.Dynamic()));
        var customers = Builders<Order>.SearchFacet.String("customers", x => x.Customer);

        // Act
        var filters = await host.Vault.Orders.SearchFiltersAsync();

        // Assert
        await Verify(new
        {
            filters.Filter,
            filters.MustNot,
            filters.PostFilter,
            filters.CanCount,
            filters.MatchesNothing,
            Applied = Render(host, filters.Apply(TextSearch)),
            Facet = Render(host, filters.Apply(Builders<Order>.Search.Facet(TextSearch, customers))),
            FacetsAlone = Render(host, filters.Apply(Builders<Order>.Search.Facet(customers)))
        });
    }

    [Test]
    public async Task SearchFiltersAsync_NoQueryFilters_LeavesTheSearchAsItIs()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .Collection(x => x.Orders, orders => orders.SearchIndex("default", index => index.Dynamic())));
        var filters = await host.Vault.Orders.SearchFiltersAsync();

        // Act
        var applied = filters.Apply(TextSearch);

        // Assert
        await Assert.That(applied).IsSameReferenceAs(TextSearch);
    }

    [Test]
    [Arguments(nameof(IVaultCollection<Patient>.SearchAsync))]
    [Arguments(nameof(IVaultCollection<Patient>.SearchMetaAsync))]
    [Arguments(nameof(IVaultCollection<Patient>.VectorSearchAsync))]
    [Arguments(nameof(IVaultCollection<Patient>.SearchFiltersAsync))]
    public async Task Search_EncryptedCollection_ThrowsNotSupportedException(string method)
    {
        // Arrange
        await using var host = ClinicHost();
        var patients = host.Vault.Patients;
        Func<Task> search = method switch
        {
            nameof(IVaultCollection<Patient>.SearchAsync) => () => patients.SearchAsync(Builders<Patient>.Search.Text(x => x.Name, "ada")).AsTask(),
            nameof(IVaultCollection<Patient>.SearchMetaAsync) => () => patients.SearchMetaAsync(Builders<Patient>.Search.Text(x => x.Name, "ada")).AsTask(),
            nameof(IVaultCollection<Patient>.VectorSearchAsync) => () => patients.VectorSearchAsync("Embedding", Vector, 10).AsTask(),
            _ => () => patients.SearchFiltersAsync().AsTask()
        };

        // Act & Assert
        await ThrowsTask(search).IgnoreStackTrace();
    }

    [Test]
    public async Task SearchIndex_OnAnEncryptedCollection_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = ClinicHost(patients => patients.SearchIndex("default", index => index.Dynamic()));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    [Arguments("search")]
    [Arguments("search meta")]
    [Arguments("vector field")]
    [Arguments("vector")]
    [Arguments("limit")]
    [Arguments("index name")]
    public async Task Search_ArgumentMissing_ThrowsArgumentException(string argument)
    {
        // Arrange
        await using var host = SearchHost(_ => { });
        var orders = host.Vault.Orders;
        Func<Task> search = argument switch
        {
            "search" => () => orders.SearchAsync(null!).AsTask(),
            "search meta" => () => orders.SearchMetaAsync(null!).AsTask(),
            "vector field" => () => orders.VectorSearchAsync((FieldDefinition<Order>)null!, Vector, 10).AsTask(),
            "vector" => () => orders.VectorSearchAsync("Embedding", null!, 10).AsTask(),
            "limit" => () => orders.VectorSearchAsync("Embedding", Vector, 0).AsTask(),
            _ => () => orders.SearchFiltersAsync(" ").AsTask()
        };

        // Act & Assert
        await Assert.That(search).Throws<ArgumentException>();
    }

    private static VaultHost<ShopVault> SearchHost(Action<IVaultCollectionBuilder<Order, int>> orders,
        LogSink? sink = null) =>
        new(vault => vault
                .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
                .UseMultiTenancy((ITenantOwned x) => x.TenantId, _ => "t-1")
                .Collection(x => x.Orders, orders),
            services => services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                if (sink is not null)
                {
                    logging.AddProvider(sink);
                }
            }));

    private static VaultHost<ClinicVault> ClinicHost(Action<IVaultCollectionBuilder<Patient, int>>? patients = null) =>
        new(vault =>
        {
            ((VaultModelBuilderBase)vault).UseEncryption(new PlainEncryption());
            vault.Collection(x => x.Patients, collection =>
            {
                ((CollectionModelBuilder<Patient>)collection).Encrypt((Expression<Func<Patient, string>>)(x => x.Ssn), new object());
                patients?.Invoke(collection);
            });
        });

    private static BsonDocument Render(VaultHost<ShopVault> host,
        SearchDefinition<Order> search)
    {
        var collection = host.Vault.Orders.MongoCollection;
        return search.Render(new RenderArgs<Order>(collection.DocumentSerializer, collection.Settings.SerializerRegistry));
    }
}
