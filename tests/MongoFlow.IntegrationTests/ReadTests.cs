using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using TUnit.Assertions.Enums;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Reads through a vault collection return only what its query filters show, and inside a transaction they see its
/// writes before the commit:
/// <list type="number">
/// <item><c>QueryAsync</c>, <c>FindAsync</c> and <c>AggregateAsync</c> start from the query filters;</item>
/// <item><c>GetByKeyAsync</c> finds a document by <c>_id</c>, a member key or a composite key, unless the query filters
/// hide it;</item>
/// <item>a query joined with another collection's query joins only what that query's filters show;</item>
/// <item>every read runs in the scope's transaction, when one is open.</item>
/// </list>
/// </summary>
public partial class ReadTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task QueryAsync_QueryFilters_LimitTheResults()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var ids = await (await host.Vault.Orders.QueryAsync()).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(ids).IsEquivalentTo([1, 3], CollectionOrdering.Matching);
    }

    [Test]
    public async Task FindAsync_FilterAndQueryFilters_FindWhatBothMatch()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var found = await (await host.Vault.Orders.FindAsync(x => x.Total > 5)).SortBy(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(found.Select(x => x.Id)).IsEquivalentTo([1, 3], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AggregateAsync_QueryFilters_MatchFirst()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var found = await (await host.Vault.Orders.AggregateAsync()).SortBy(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(found.Select(x => x.Id)).IsEquivalentTo([1, 3], CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetByKeyAsync_StoredKey_ReturnsTheDocument()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var order = await host.Vault.Orders.GetByKeyAsync(1);

        // Assert
        await Verify(order);
    }

    [Test]
    public async Task GetByKeyAsync_MissingKey_ReturnsNull()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var order = await host.Vault.Orders.GetByKeyAsync(99);

        // Assert
        await Assert.That(order).IsNull();
    }

    [Test]
    public async Task GetByKeyAsync_DocumentTheQueryFiltersHide_ReturnsItOnlyWithTheirFeatureOff()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var hidden = await host.Vault.Orders.GetByKeyAsync(2);
        var shown = await host.Vault.Orders.Without(HiddenFeature.Key).GetByKeyAsync(2);

        // Assert
        await Assert.That(hidden).IsNull();
        await Assert.That(shown?.Customer).IsEqualTo(HiddenFeature.Customer);
    }

    [Test]
    public async Task GetByKeyAsync_MemberKey_ReturnsTheDocument()
    {
        // Arrange
        await using var host = Mongo.Host<InsuranceVault>();
        await host.SeedAsync("Policies", new Policy { Id = 1, Number = "P-1", Holder = "ada" }, new Policy { Id = 2, Number = "P-2", Holder = "bob" });

        // Act
        var policy = await host.Vault.Policies.GetByKeyAsync("P-2");

        // Assert
        await Verify(policy);
    }

    [Test]
    public async Task GetByKeyAsync_CompositeKey_ReturnsTheDocument()
    {
        // Arrange
        await using var host = Mongo.Host<InsuranceVault>();
        await host.SeedAsync("Tokens",
            new LoginToken { Id = 1, UserId = "u-1", Provider = "github", Value = "first" },
            new LoginToken { Id = 2, UserId = "u-1", Provider = "google", Value = "second" });

        // Act
        var token = await host.Vault.Tokens.GetByKeyAsync(new TokenKey("u-1", "google"));

        // Assert
        await Verify(token);
    }

    [Test]
    public async Task Reads_InATransaction_SeeItsUncommittedWrites()
    {
        // Arrange
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 4, Customer = "dee", Total = 40 });
        await host.Vault.SaveAsync();
        await using var outside = host.CreateScope();

        // Act
        var reads = new
        {
            Query = await (await host.Vault.Orders.QueryAsync()).Select(x => x.Id).ToListAsync(),
            Find = await (await host.Vault.Orders.FindAsync(x => x.Id == 4)).ToListAsync(),
            Aggregate = await (await host.Vault.Orders.AggregateAsync()).Match(x => x.Id == 4).ToListAsync(),
            ByKey = await host.Vault.Orders.GetByKeyAsync(4),
            SeenOutside = await outside.ServiceProvider.GetRequiredService<ShopVault>().Orders.GetByKeyAsync(4) is not null
        };

        // Assert
        await Verify(reads);
    }

    private async Task<VaultHost<ShopVault>> SeededAsync()
    {
        var host = Mongo.Host<ShopVault>(vault => vault.AddFeature(new HiddenFeature()));
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = HiddenFeature.Customer, Total = 20 },
            new Order { Id = 3, Customer = "bob", Total = 30 });

        return host;
    }
}
