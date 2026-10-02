using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class InterceptorTests
{
    [Test]
    public async Task AddCondition_StoredDocumentDoesNotMatch_LeavesItUnwrittenAndTheSaveGoesOn()
    {
        // Arrange
        var recorder = new ConditionRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new OrderCondition(Builders<Order>.Filter.Eq(x => x.Customer, "bob")))
            .AddInterceptor(recorder));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 20));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, recorder.Conditions, recorder.Results, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task AddCondition_FromTwoInterceptors_JoinsThem()
    {
        // Arrange
        var recorder = new ConditionRecorder();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new OrderCondition(Builders<Order>.Filter.Eq(x => x.Customer, "ada")))
            .AddInterceptor(new OrderCondition(Builders<Order>.Filter.Gt(x => x.Total, 5)))
            .AddInterceptor(recorder));
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(1, Builders<Order>.Update.Set(x => x.Total, 20));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { recorder.Conditions, Stored = await host.StoredAsync("Orders") });
    }

    [Test]
    public async Task AddCondition_ToAnInsert_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new InsertCondition()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }
}
