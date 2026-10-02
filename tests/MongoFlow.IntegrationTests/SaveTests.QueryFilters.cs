using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class SaveTests
{
    [Test]
    public async Task SaveAsync_ReplaceOfADocumentTheQueryFiltersHide_ChangesNothing()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddFeature(new HiddenFeature()));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = HiddenFeature.Customer, Total = 10 });
        host.Vault.Orders.Replace(new Order { Id = 1, Customer = "ada", Total = 20 });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_UpdateManyUnderQueryFilters_ChangesOnlyWhatTheyShow()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddFeature(new HiddenFeature()));
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = HiddenFeature.Customer, Total = 20 });
        host.Vault.Orders.UpdateMany(x => x.Total > 0, Builders<Order>.Update.Set(x => x.Total, 0));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SaveAsync_ThroughAViewWithTheFeatureOff_ReachesWhatItHides()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddFeature(new HiddenFeature()));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = HiddenFeature.Customer, Total = 10 });
        host.Vault.Orders.Without(HiddenFeature.Key).UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 0));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_WritesUnderTheSameFeatures_ResolveTheQueryFiltersOnce()
    {
        // Arrange — three writes through the collection, one through a view, and an insert, which isn't filtered.
        var resolved = 0;
        await using var host = Mongo.Host<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders.QueryFilter(_ =>
        {
            resolved++;
            return x => x.Customer != HiddenFeature.Customer;
        })));
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 0));
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Total, 0));
        host.Vault.Orders.DeleteByKey(3);
        host.Vault.Orders.Without(HiddenFeature.Key).DeleteByKey(4);
        host.Vault.Orders.Add(new Order { Id = 5, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(resolved).IsEqualTo(2);
    }
}
