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
    public async Task QueryFilter_LambdaExpressions_AreRewrittenOverTheDocumentType()
    {
        // Arrange — each lambda's parameter type is only known at run time: an interface, the document, or object.
        LambdaExpression tenant = (Expression<Func<ITenantOwned, bool>>)(x => x.TenantId == "t-1");
        LambdaExpression total = (Expression<Func<Order, bool>>)(x => x.Total > 10);
        LambdaExpression message = (Expression<Func<AuditEntry, bool>>)(x => x.Message != "");
        LambdaExpression everything = (Expression<Func<object, bool>>)(_ => true);
        await using var host = new VaultHost<ShopVault>(vault => vault
            .Collection(x => x.Orders, orders => orders
                .QueryFilter(tenant)
                .QueryFilter(_ => total)
                .QueryFilter((_, _) => ValueTask.FromResult(everything)))
            .Collection(x => x.Audit, audit => audit
                .QueryFilter(tenant)
                .QueryFilter(_ => message)
                .QueryFilter((_, _) => ValueTask.FromResult(everything))));

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
    public async Task QueryFilter_LambdaExpressionOverObjectReturningFalse_MatchesNothing()
    {
        // Arrange
        LambdaExpression nothing = (Expression<Func<object, bool>>)(_ => false);
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders
            .QueryFilter(x => x.Total > 10)
            .QueryFilter(_ => nothing)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    [MethodDataSource(nameof(LambdaExpressionsThatCantFilterOrders))]
    public async Task QueryFilter_LambdaExpressionThatCantFilterTheDocuments_ThrowsArgumentException(string lambda,
        LambdaExpression filter)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection(x => x.Orders, orders => orders.QueryFilter(filter)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace().UseParameters(lambda);
    }

    [Test]
    public async Task QueryFilter_PerQueryLambdaExpressionThatCantFilterTheDocuments_FailsTheQuery()
    {
        // Arrange
        LambdaExpression total = (Expression<Func<Order, decimal>>)(x => x.Total);
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection(x => x.Orders, orders => orders.QueryFilter(_ => total)));

        // Act & Assert
        await ThrowsValueTask(() => host.Vault.Orders.QueryAsync()).IgnoreStackTrace();
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

    public static IEnumerable<Func<(string, LambdaExpression)>> LambdaExpressionsThatCantFilterOrders()
    {
        yield return () => ("another type", (Expression<Func<AuditEntry, bool>>)(x => x.Message != ""));
        yield return () => ("not a bool", (Expression<Func<Order, decimal>>)(x => x.Total));
        yield return () => ("two parameters", (Expression<Func<Order, Order, bool>>)((x, y) => x.Id == y.Id));
    }
}
