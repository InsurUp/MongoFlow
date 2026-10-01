using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class SaveTests
{
    [Test]
    public async Task SaveAsync_SameDocumentAddedTwice_InsertsItOnce()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        var order = new Order { Id = 1, Customer = "ada", Total = 10 };
        host.Vault.Orders.Add(order);
        host.Vault.Orders.Add(order);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(new SaveResult(1, 0, 0, 0));
    }

    [Test]
    public async Task SaveAsync_Again_HasNothingLeftToWrite()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
    }

    [Test]
    public async Task SaveAsync_AfterAFailedSave_HasNothingLeftToWrite()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).Throws<MongoException>();

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
    }

    [Test]
    public async Task SaveAsync_ManyOperations_WritesThemAll()
    {
        // Arrange
        const int Count = 1000;
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.AddRange(Enumerable.Range(1, Count).Select(id => new Order { Id = id, Customer = "ada", Total = id }));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Inserted).IsEqualTo(Count);
        await Assert.That(await host.Database.GetCollection<Order>("Orders").CountDocumentsAsync(FilterDefinition<Order>.Empty)).IsEqualTo(Count);
    }
}
