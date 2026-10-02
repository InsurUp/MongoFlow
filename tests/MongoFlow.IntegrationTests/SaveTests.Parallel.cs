using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class SaveTests
{
    [Test]
    public async Task SaveAsync_WritesQueuedByParallelReads_WritesThemAll()
    {
        // Arrange — each task reads its order and queues an update, the way Task.WhenAll over a vault would.
        const int Count = 200;
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", [.. Enumerable.Range(1, Count).Select(id => new Order { Id = id, Customer = "ada", Total = 10 })]);
        var vault = host.Vault;
        await Task.WhenAll(Enumerable.Range(1, Count).Select(async id =>
        {
            var order = await vault.Orders.GetByKeyAsync(id);
            vault.Orders.Update(order!, Builders<Order>.Update.Inc(x => x.Total, 1));
        }));

        // Act
        var result = await vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(new SaveResult(0, Count, Count, 0));
    }

    [Test]
    public async Task SaveAsync_WhileAnotherSaveRuns_ThrowsAndLeavesItToFinish()
    {
        // Arrange — the first save holds in its interceptor until the second has been tried.
        var gate = new Gate();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(gate));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        var first = host.Vault.SaveAsync();
        await gate.Entered.Task;

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
        gate.Release.SetResult();
        await Assert.That((await first).Inserted).IsEqualTo(1);
    }
}
