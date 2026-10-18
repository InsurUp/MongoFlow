using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Search;

namespace MongoFlow.Tests.Indexes;

/// <summary>
/// What <see cref="VaultIndexSync"/> asks the server:
/// <list type="number">
/// <item>which collections are sharded, with <c>$listClusterCatalog</c>, so a server that doesn't have it, before MongoDB
/// 8.0.10, fails syncing with a clear message;</item>
/// <item>a collection's search indexes: none on a server without search, unless the collection declares some; any other
/// failure to list them fails syncing.</item>
/// </list>
/// </summary>
public class VaultIndexSyncTests
{
    // The server's codes for an aggregation stage it doesn't know, for a server without search, and for a missing namespace.
    private const int UnrecognizedStage = 40324;
    private const int SearchNotEnabled = 31082;
    private const int NamespaceNotFound = 26;

    private readonly Mock<IMongoDatabase> _database = IMongoDatabase.Mock();
    private readonly Mock<IMongoCollection<BsonDocument>> _collection = IMongoCollection<BsonDocument>.Mock();
    private readonly Mock<IMongoSearchIndexManager> _indexes = IMongoSearchIndexManager.Mock();

    public VaultIndexSyncTests()
    {
        _database.GetCollection<BsonDocument>(Any(), Any()).Returns(_collection.Object);
        _collection.SearchIndexes.Returns(_indexes.Object);
    }

    [Test]
    public async Task ShardKeysAsync_ServerWithoutListClusterCatalog_ThrowsInvalidOperationException()
    {
        // Arrange
        _database.AggregateAsync<BsonDocument>(Any(), Any(), Any()).Throws(new MongoCommandException(
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))),
            "Unrecognized pipeline stage name: '$listClusterCatalog'",
            [],
            new BsonDocument { { "ok", 0 }, { "code", UnrecognizedStage } }));

        // Act
        var exception = await Assert.That(() => (Task)VaultIndexSync.ShardKeysAsync(_database.Object, ["db.Orders"], CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { exception!.Message, Cause = exception.InnerException!.Message });
    }

    [Test]
    public async Task ListSearchAsync_ServerWithoutSearch_ListsNone()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Throws(CommandFailed(SearchNotEnabled));

        // Act
        var listed = await VaultIndexSync.ListSearchAsync(_database.Object, "Orders", declares: false, CancellationToken.None);

        // Assert
        await Assert.That(listed).IsEmpty();
    }

    [Test]
    [Arguments(SearchNotEnabled, true)]
    [Arguments(NamespaceNotFound, false)]
    public async Task ListSearchAsync_ListingFailsOtherwise_ThrowsTheServersError(int code,
        bool declares)
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Throws(CommandFailed(code));

        // Act & Assert
        await Assert.That(() => (Task)VaultIndexSync.ListSearchAsync(_database.Object, "Orders", declares, CancellationToken.None))
            .ThrowsExactly<MongoCommandException>();
    }

    private static MongoCommandException CommandFailed(int code) =>
        new(new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))),
            $"Command failed with code {code}.",
            [],
            new BsonDocument { { "ok", 0 }, { "code", code } });
}
