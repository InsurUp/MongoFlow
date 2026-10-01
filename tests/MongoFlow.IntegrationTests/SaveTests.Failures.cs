using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class SaveTests
{
    [Test]
    public async Task SaveAsync_AWriteFails_WritesNothing()
    {
        // Arrange — the second insert repeats a stored key.
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SaveAsync_Cancelled_ThrowsAndWritesNothing()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync(new CancellationToken(canceled: true))).Throws<OperationCanceledException>();

        // Assert
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }
}
