using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Search;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexSetup.DropAsync"/>, syncing indexes, drops a search index no vault declares: one the server fails
/// to drop fails the migrator with the server's error.
/// </summary>
public class SearchIndexSetupTests
{
    private readonly Mock<IMongoSearchIndexManager> _indexes = IMongoSearchIndexManager.Mock();

    [Test]
    public async Task DropAsync_ServerFailsTheDrop_ThrowsItsError()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        var model = host.Services.GetRequiredService<VaultModelProvider<ShopVault>>().Model;
        _indexes.DropOneAsync(Any(), Any()).Throws(new MongoCommandException(
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))),
            "Command failed with code 2.",
            [],
            new BsonDocument { { "ok", 0 }, { "code", 2 } }));

        // Act & Assert
        await Assert.That(() => new SearchIndexSetup(TimeProvider.System).DropAsync(model, "Orders", _indexes.Object,
                BsonDocument.Parse("{ name: 'byHand', type: 'search', latestDefinition: { mappings: { dynamic: true } } }"),
                CancellationToken.None))
            .ThrowsExactly<MongoCommandException>();
    }
}
