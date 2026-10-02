using Microsoft.Extensions.DependencyInjection;
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
    public async Task SaveAsync_DocumentAnotherTransactionIsChanging_FailsWithATransientWriteConflict()
    {
        // Arrange — another request's open transaction has changed the order and not committed yet.
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        await using var other = host.CreateScope();
        await using var transaction = await other.ServiceProvider.GetRequiredService<IVaultTransactionManager>().BeginAsync();
        var otherVault = other.ServiceProvider.GetRequiredService<ShopVault>();
        otherVault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 20));
        await otherVault.SaveAsync();
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 30));

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Assert.That(exception!.HasErrorLabel("TransientTransactionError")).IsTrue();
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
