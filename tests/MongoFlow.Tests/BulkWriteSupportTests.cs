using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="BulkWriteSupport.EnsureAsync"/>: a save needs client bulk writes, so a server older than MongoDB 8.0 fails
/// it with a clear message; a server that has them is asked once per client.
/// </summary>
public class BulkWriteSupportTests
{
    private readonly Mock<IMongoClient> _client = IMongoClient.Mock();
    private readonly Mock<IMongoDatabase> _database = IMongoDatabase.Mock();

    public BulkWriteSupportTests()
    {
        _database.Client.Returns(_client.Object);
    }

    [Test]
    public async Task EnsureAsync_ServerOlderThan8_ThrowsNotSupportedException()
    {
        // Arrange — MongoDB 7.0
        Hello(new BsonDocument("maxWireVersion", 21));

        // Act & Assert
        await ThrowsTask(() => BulkWriteSupport.EnsureAsync(_database.Object, CancellationToken.None)).IgnoreStackTrace();
    }

    [Test]
    public async Task EnsureAsync_NoWireVersion_ThrowsNotSupportedException()
    {
        // Arrange
        Hello([]);

        // Act & Assert
        await Assert.That(() => BulkWriteSupport.EnsureAsync(_database.Object, CancellationToken.None))
            .ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task EnsureAsync_Server8_AsksOncePerClient()
    {
        // Arrange — MongoDB 8.0
        Hello(new BsonDocument("maxWireVersion", 25));

        // Act
        await BulkWriteSupport.EnsureAsync(_database.Object, CancellationToken.None);
        await BulkWriteSupport.EnsureAsync(_database.Object, CancellationToken.None);

        // Assert
        _database.RunCommandAsync<BsonDocument>(Any(), Any(), Any()).WasCalled(Times.Once);
    }

    private void Hello(BsonDocument reply) =>
        _database.RunCommandAsync<BsonDocument>(Any(), Any(), Any()).Returns(reply);
}
