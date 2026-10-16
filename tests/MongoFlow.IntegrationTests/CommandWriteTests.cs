using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A vault on a client that encrypts, which the driver refuses client bulk writes, saves with collection commands:
/// <list type="number">
/// <item>every kind of write lands in queue order, inserts into one collection that follow each other in one command,
/// and the result sums what they changed;</item>
/// <item>a write a unique index refuses fails the save with <see cref="DuplicateKeyException"/>, naming it, and nothing
/// of the save is written.</item>
/// </list>
/// </summary>
public class CommandWriteTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_EveryKindOfWrite_WritesThemInQueueOrder()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 }, new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.AddRange([new Order { Id = 3, Customer = "cy", Total = 30 }, new Order { Id = 4, Customer = "di", Total = 40 }]);
        host.Vault.Audit.Add(new AuditEntry { Message = "added" });
        host.Vault.Orders.Add(new Order { Id = 5, Customer = "ed", Total = 50 });
        host.Vault.Orders.Replace(new Order { Id = 1, Customer = "ada lovelace", Total = 11 });
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Total, 21));
        host.Vault.Orders.UpdateMany(x => x.Total >= 30, Builders<Order>.Update.Inc(x => x.Total, 1));
        host.Vault.Orders.DeleteByKey(4);
        host.Vault.Orders.DeleteMany(x => x.Customer == "ed");

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
    public async Task SaveAsync_InsertRepeatingAKey_ThrowsDuplicateKeyExceptionAndWritesNothing()
    {
        // Arrange — the second insert, in the same command as the first, repeats a stored key.
        await using var host = Host();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada" });
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob" });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada again" });

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert
        await Verify(new { exception!.Operation.Key, exception.InnerException!.GetType().Name, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task SaveAsync_ReplaceOrUpdateOntoAUniqueValue_ThrowsDuplicateKeyException()
    {
        // Arrange — each customer has one order, by a unique index.
        await using var host = Host();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada" }, new Order { Id = 2, Customer = "bob" });
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Customer), new CreateIndexOptions { Unique = true }));

        // Act
        host.Vault.Orders.Replace(new Order { Id = 2, Customer = "ada" });
        var replaced = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Customer, "ada"));
        var updated = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert
        await Verify(new
        {
            Replaced = new { replaced!.Operation.Kind, replaced.InnerException!.GetType().Name },
            Updated = new { updated!.Operation.Kind, updated.InnerException!.GetType().Name },
            Stored = await host.StoredAsync("Orders")
        });
    }

    private VaultHost<ShopVault> Host() =>
        new(Mongo.EncryptingClient, Mongo.EncryptingClient.GetDatabase($"t{Guid.NewGuid():N}"));
}
