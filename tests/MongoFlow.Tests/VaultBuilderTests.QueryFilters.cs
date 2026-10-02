using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    public async Task QueryFilter_OnACollection_FiltersItsReads()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection(x => x.Orders, orders => orders.QueryFilter(x => x.Total > 10)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_PerQuery_ReadsTheRequestsServices()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(
            vault => vault.Collection(x => x.Orders, orders => orders
                .QueryFilter(services => x => x.Customer == services.GetRequiredService<CurrentCustomer>().Name)),
            services => services.AddScoped(_ => new CurrentCustomer { Name = "ada" }));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_Async_IsResolvedWhenTheQueryRuns()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders
            .QueryFilter(async (_, _) =>
            {
                await Task.Yield();
                return (Expression<Func<Order, bool>>)(x => x.Customer == "ada");
            })));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_OnTheVault_FiltersOnlyTheDocumentsItTargets()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.QueryFilter<ISoftDeletable>(x => !x.IsDeleted));

        // Act
        var queries = new
        {
            Orders = (await host.Vault.Orders.QueryAsync()).ToString(),
            Audit = (await host.Vault.Audit.QueryAsync()).ToString()
        };

        // Assert
        await Verify(queries);
    }

    [Test]
    public async Task QueryFilter_OnTheVaultPerQueryAndAsync_FiltersEveryTargetedCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .QueryFilter<ITenantOwned>(_ => x => x.TenantId == "t-1")
            .QueryFilter<ITenantOwned>((_, _) => ValueTask.FromResult<Expression<Func<ITenantOwned, bool>>>(x => x.TenantId != null)));

        // Act
        var queries = new
        {
            Orders = (await host.Vault.Orders.QueryAsync()).ToString(),
            Audit = (await host.Vault.Audit.QueryAsync()).ToString()
        };

        // Assert
        await Verify(queries);
    }

    [Test]
    public async Task QueryFilter_ConstantTrue_LeavesTheQueryUnfiltered()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection(x => x.Orders, orders => orders.QueryFilter(_ => _ => true)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_ConstantFalse_MatchesNothing()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders
            .QueryFilter(x => x.Total > 10)
            .QueryFilter(_ => _ => false)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_OfAFeature_IsSwitchedOffWithIt()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .AddFeature<ActiveOnlyFeature>()
            .Collection(x => x.Orders, orders => orders.QueryFilter(x => x.Total > 10)));

        // Act
        var queries = new
        {
            On = (await host.Vault.Orders.QueryAsync()).ToString(),
            Off = (await host.Vault.Orders.Without(ActiveOnlyFeature.Key).QueryAsync()).ToString(),
            OtherOff = (await host.Vault.Orders.Without(TenantFeature.Key).QueryAsync()).ToString()
        };

        // Assert
        await Verify(queries);
    }

    [Test]
    public async Task QueryFilter_OfAFeatureTheCollectionOptedOutOf_DoesNotApply()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .AddFeature<ActiveOnlyFeature>()
            .Collection(x => x.Orders, orders => orders.Without(ActiveOnlyFeature.Key)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_PerQueryWithAFeatureOff_LeavesOutOnlyTheFeaturesFilter()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .AddFeature<ActiveOnlyFeature>()
            .Collection(x => x.Orders, orders => orders.QueryFilter(_ => x => x.Customer == "ada")));

        // Act
        var query = await host.Vault.Orders.Without(ActiveOnlyFeature.Key).QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryFilter_AsyncWithAFeatureOff_LeavesOutOnlyTheFeaturesFilter()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault
            .AddFeature<ActiveOnlyFeature>()
            .Collection(x => x.Orders, orders => orders
                .QueryFilter((_, _) => ValueTask.FromResult<Expression<Func<Order, bool>>>(x => x.Customer == "ada"))));

        // Act
        var query = await host.Vault.Orders.Without(ActiveOnlyFeature.Key).QueryAsync();

        // Assert
        await Verify(query.ToString());
    }
}
