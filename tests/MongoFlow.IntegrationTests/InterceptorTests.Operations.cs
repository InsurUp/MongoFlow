using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class InterceptorTests
{
    [Test]
    public async Task SavingAsync_Operations_DescribeTheirWrites()
    {
        // Arrange
        var describer = new OperationDescriber();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(describer));
        var order = new Order { Id = 1, Customer = "ada", Total = 10 };
        host.Vault.Orders.Add(order);
        host.Vault.Orders.Replace(order);
        host.Vault.Orders.Update(order, Builders<Order>.Update.Set(x => x.Total, 20));
        host.Vault.Orders.UpdateMany(x => x.Total > 5, Builders<Order>.Update.Set(x => x.Customer, "many"));
        host.Vault.Orders.Delete(order);
        host.Vault.Orders.DeleteMany(x => x.Total > 5);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(describer.Described);
    }

    [Test]
    public async Task SavingAsync_ReplacingAnOperation_WritesTheReplacement()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new UpdateStamper("stamped")));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 20));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SavingAsync_ReplacingOperationsInReverse_WritesEachReplacement()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new UpdateStamper("stamped", reverse: true)));
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = "bob", Total = 20 },
            new Order { Id = 3, Customer = "cy", Total = 30 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Inc(x => x.Total, 1));
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Inc(x => x.Total, 1));
        host.Vault.Orders.UpdateByKey(3, Builders<Order>.Update.Inc(x => x.Total, 1));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SavingAsync_RemovingAnOperation_LeavesItUnwritten()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new AuditRemover()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Audit.Add(new AuditEntry { Message = "created" });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Audit = await host.StoredAsync("Audit") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SavingAsync_RemovingEveryOperation_WritesNothingButRunsTheHooks()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new AuditRemover())
            .AddInterceptor(new RecordingInterceptor("last", log)));
        host.Vault.Audit.Add(new AuditEntry { Message = "created" });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, log.Entries });
    }

    [Test]
    public async Task SavingAsync_QueueingAWrite_JoinsTheSaveForTheInterceptorsAfter()
    {
        // Arrange
        var counter = new OperationRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new AuditWriter("before the count"))
            .AddInterceptor(counter)
            .AddInterceptor(new AuditWriter("after the count")));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { counter.Seen, Audit = (await host.StoredAsync("Audit")).Select(entry => entry["Message"]) });
    }

    [Test]
    public async Task SavingAsync_ReplacingWithAnotherCollectionsOperation_ThrowsArgumentException()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new Misuse(Misuse.Kind.ReplaceAcrossCollections)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Audit.Add(new AuditEntry { Message = "created" });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task SavingAsync_ReplacingAnOperationOfAnotherSave_ThrowsArgumentException()
    {
        // Arrange
        var misuse = new Misuse(Misuse.Kind.ReplaceFromAnotherSave);
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(misuse));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    [Arguments(Misuse.Kind.ReplaceNullOperation)]
    [Arguments(Misuse.Kind.ReplaceWithNull)]
    [Arguments(Misuse.Kind.RemoveNull)]
    public async Task SavingAsync_NullOperation_ThrowsArgumentNullException(Misuse.Kind kind)
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new Misuse(kind)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task SavedAsync_ReplacingAnOperation_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new Misuse(Misuse.Kind.ReplaceOnceSaved)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task SavedAsync_RemovingAnOperation_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new Misuse(Misuse.Kind.RemoveOnceSaved)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
    }
}
