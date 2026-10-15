using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
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
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SaveAsync_InsertRepeatsAStoredKey_ThrowsDuplicateKeyExceptionNamingTheInsert()
    {
        // Arrange — the refused insert is the third write, after one to another collection.
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Audit.Add(new AuditEntry { Message = "created" });
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert
        await Verify(new
        {
            exception!.Message,
            exception.Operation.Kind,
            exception.Operation.Collection.PropertyName,
            exception.Operation.Key,
            InnerException = exception.InnerException?.GetType().Name,
            Orders = await host.StoredAsync("Orders"),
            Audit = await host.StoredAsync("Audit")
        }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_UpdateRepeatsAValueAUniqueIndexHolds_ThrowsDuplicateKeyExceptionNamingTheUpdate()
    {
        // Arrange — each customer has one order.
        await using var host = Mongo.Host<ShopVault>();
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Customer),
            new CreateIndexOptions { Unique = true }));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 }, new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Customer, "ada"));

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert
        await Verify(new
        {
            exception!.Message,
            exception.Operation.Kind,
            exception.Operation.Key,
            Stored = await host.StoredAsync("Orders")
        });
    }

    [Test]
    public async Task SaveAsync_WriteRefusedForAnotherReason_ThrowsTheDriversException()
    {
        // Arrange — the stored customer is a string, which can't be incremented.
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(1, new BsonDocumentUpdateDefinition<Order>(new BsonDocument("$inc", new BsonDocument("Customer", 1))));

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Assert.That(exception!.WriteErrors.Values.Single().Category).IsEqualTo(ServerErrorCategory.Uncategorized);
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
