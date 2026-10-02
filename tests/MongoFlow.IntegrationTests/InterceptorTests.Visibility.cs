namespace MongoFlow.IntegrationTests;

public partial class InterceptorTests
{
    [Test]
    public async Task Operations_OfAnInterceptorOnACollection_AreOnlyThatCollections()
    {
        // Arrange
        var vault = new OperationRecorder();
        var orders = new OperationRecorder();
        await using var host = Mongo.Host<ShopVault>(builder => builder
            .AddInterceptor(vault)
            .Collection(x => x.Orders, collection => collection.AddInterceptor(orders)));
        QueueOrderAndAudit(host.Vault);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Vault = vault.Seen, Orders = orders.Seen });
    }

    [Test]
    public async Task Operations_OfAnInterceptorNarrowedWithFor_AreOnlyTheCollectionsItKeeps()
    {
        // Arrange
        var keyless = new OperationRecorder();
        var none = new OperationRecorder();
        await using var host = Mongo.Host<ShopVault>(builder => builder
            .AddInterceptor(keyless, interceptor => interceptor.For(collection => collection.KeyType is null))
            .AddInterceptor(none, interceptor => interceptor
                .For(collection => collection.KeyType is null)
                .For(collection => collection.DocumentType == typeof(Order))));
        QueueOrderAndAudit(host.Vault);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Keyless = keyless.Seen, None = none.Seen }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Operations_OfAFeaturesInterceptor_LeaveOutThoseQueuedWithTheFeatureOff()
    {
        // Arrange
        var counter = new OperationRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddFeature(new CountingFeature(counter)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.Without(CountingFeature.Key).Add(new Order { Id = 2, Customer = "bob", Total = 20 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(counter.Seen);
    }

    [Test]
    public async Task Operations_OfAFeaturesInterceptor_LeaveOutCollectionsThatOptedOut()
    {
        // Arrange
        var counter = new OperationRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddFeature(new CountingFeature(counter))
            .Collection(x => x.Orders, orders => orders.Without(CountingFeature.Key)));
        QueueOrderAndAudit(host.Vault);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(counter.Seen);
    }

    private static void QueueOrderAndAudit(ShopVault vault)
    {
        vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        vault.Audit.Add(new AuditEntry { Message = "created" });
    }
}
