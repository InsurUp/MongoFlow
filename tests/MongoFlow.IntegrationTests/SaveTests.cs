using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="IMongoVault.SaveAsync"/> writes what was queued, as one ordered bulk write in a transaction:
/// <list type="number">
/// <item>each kind of write lands as queued, by <c>_id</c>, by a member key or by a composite key;</item>
/// <item>writes run in queue order, and the result sums what they changed while each operation gets its own;</item>
/// <item>the queue empties with every save, whether it succeeds or fails, and a document added twice is inserted
/// once;</item>
/// <item>a failing write fails the save, and nothing of it is written, as when another transaction is changing a
/// document;</item>
/// <item>writes by key or filter stay within what the query filters show, resolved once per save.</item>
/// </list>
/// </summary>
public partial class SaveTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_Adds_InsertsTheDocuments()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.AddRange([new Order { Id = 1, Customer = "ada", Total = 10 }, new Order { Id = 2, Customer = "bob", Total = 20 }]);
        host.Vault.Audit.Add(new AuditEntry { Message = "created" });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Orders = await host.StoredAsync("Orders"),
            Audit = (await host.StoredAsync("Audit")).Select(entry => entry["Message"])
        });
    }

    [Test]
    public async Task SaveAsync_Replace_ReplacesTheDocumentWithTheSameMemberKey()
    {
        // Arrange
        await using var host = Mongo.Host<InsuranceVault>();
        await host.SeedAsync("Policies", new Policy { Id = 1, Number = "P-1", Holder = "ada" }, new Policy { Id = 2, Number = "P-2", Holder = "bob" });
        host.Vault.Policies.Replace(new Policy { Id = 1, Number = "P-1", Holder = "ada lovelace" });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Policies") });
    }

    [Test]
    public async Task SaveAsync_Replace_ReplacesTheDocumentWithTheSameCompositeKey()
    {
        // Arrange
        await using var host = Mongo.Host<InsuranceVault>();
        await host.SeedAsync("Tokens",
            new LoginToken { Id = 1, UserId = "u-1", Provider = "github", Value = "old" },
            new LoginToken { Id = 2, UserId = "u-1", Provider = "google", Value = "old" });
        host.Vault.Tokens.Replace(new LoginToken { Id = 1, UserId = "u-1", Provider = "github", Value = "new" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Tokens"));
    }

    [Test]
    public async Task SaveAsync_Update_AppliesTheDefinitionToTheStoredDocumentOnly()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        var order = new Order { Id = 1, Customer = "ada", Total = 10 };
        host.Vault.Orders.Update(order, Builders<Order>.Update.Set(x => x.Total, 99));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Orders"), Document = order });
    }

    [Test]
    public async Task SaveAsync_UpdateByKey_AppliesTheDefinition()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 }, new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Inc(x => x.Total, 5));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_UpdateMany_UpdatesEveryMatch()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = "bob", Total = 20 },
            new Order { Id = 3, Customer = "ada", Total = 30 });
        host.Vault.Orders.UpdateMany(x => x.Customer == "ada", Builders<Order>.Update.Set(x => x.Total, 0));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_DeleteAndDeleteByKey_DeleteTheDocumentsWithTheKeys()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = "bob", Total = 20 },
            new Order { Id = 3, Customer = "cy", Total = 30 });
        host.Vault.Orders.Delete(new Order { Id = 1 });
        host.Vault.Orders.DeleteByKey(3);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_DeleteMany_DeletesEveryMatch()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = "bob", Total = 20 },
            new Order { Id = 3, Customer = "ada", Total = 30 });
        host.Vault.Orders.DeleteMany(x => x.Customer == "ada");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_SeveralOperations_RunInQueueOrder()
    {
        // Arrange — each write depends on the one before it.
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 15));
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.DeleteByKey(2);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_EachOperation_GetsWhatItChanged()
    {
        // Arrange
        var recorder = new ResultRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(recorder));
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = "bob", Total = 20 },
            new Order { Id = 3, Customer = "bob", Total = 30 });
        host.Vault.Orders.Add(new Order { Id = 4, Customer = "cy", Total = 40 });
        host.Vault.Orders.Replace(new Order { Id = 1, Customer = "ada", Total = 11 });
        host.Vault.Orders.UpdateMany(x => x.Customer == "bob", Builders<Order>.Update.Set(x => x.Total, 0));
        host.Vault.Orders.UpdateByKey(99, Builders<Order>.Update.Set(x => x.Total, 0));
        host.Vault.Orders.DeleteByKey(99);
        host.Vault.Orders.DeleteMany(x => x.Customer == "bob");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(recorder.Results);
    }
}
