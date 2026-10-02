using MongoDB.Driver;

namespace MongoFlow.Tests;

public partial class VaultCollectionTests
{
    [Test]
    public async Task QueryAsyncAndFindAsync_ThroughATrackingView_ShowTheQueriesUntrackedReadsShow()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        var untrackedFind = await host.Vault.Orders.FindAsync(x => x.Total > 10);
        var trackedFind = await host.Vault.Orders.WithTracking().FindAsync(x => x.Total > 10);
        var untracked = new
        {
            Query = (await host.Vault.Orders.QueryAsync()).Where(x => x.Total > 10).ToString(),
            Find = untrackedFind.Limit(1).ToString(),
            Translated = untrackedFind.ToString(new ExpressionTranslationOptions())
        };

        // Act
        var tracked = new
        {
            Query = (await host.Vault.Orders.WithTracking().QueryAsync()).Where(x => x.Total > 10).ToString(),
            Find = trackedFind.Limit(1).ToString(),
            Translated = trackedFind.ToString(new ExpressionTranslationOptions())
        };

        // Assert
        await Assert.That(tracked).IsEqualTo(untracked);
    }
}
