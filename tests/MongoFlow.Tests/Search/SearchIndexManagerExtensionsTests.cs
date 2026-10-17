using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexManagerExtensions"/>, for indexes managed by hand: <c>CreateOrUpdateAsync</c> lists the index
/// and creates or updates it as needed; <c>WaitUntilQueryableAsync</c> returns once it's queryable, and gives up with a
/// <see cref="TimeoutException"/> after its timeout, or as canceled when its caller cancels.
/// </summary>
public class SearchIndexManagerExtensionsTests
{
    private static readonly BsonDocument Definition = BsonDocument.Parse("{ mappings: { dynamic: true } }");

    private readonly Mock<IMongoSearchIndexManager> _indexes = IMongoSearchIndexManager.Mock();

    [Test]
    public async Task CreateOrUpdateAsync_NoneListed_CreatesIt()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing());

        // Act
        var change = await _indexes.Object.CreateOrUpdateAsync("default", Definition);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Created);
    }

    [Test]
    public async Task CreateOrUpdateAsync_ListedAsGiven_LeavesIt()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing(Ready(BsonDocument.Parse("{ mappings: { dynamic: true, fields: { } } }"))));

        // Act
        var change = await _indexes.Object.CreateOrUpdateAsync("default", Definition);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Unchanged);
    }

    [Test]
    [Arguments("indexes")]
    [Arguments("name")]
    [Arguments("definition")]
    public async Task CreateOrUpdateAsync_ArgumentMissing_ThrowsArgumentException(string argument)
    {
        // Arrange
        Func<Task> create = argument switch
        {
            "indexes" => () => ((IMongoSearchIndexManager)null!).CreateOrUpdateAsync("default", Definition),
            "name" => () => _indexes.Object.CreateOrUpdateAsync(" ", Definition),
            _ => () => _indexes.Object.CreateOrUpdateAsync("default", null!)
        };

        // Act & Assert
        await Assert.That(create).Throws<ArgumentException>();
    }

    [Test]
    public async Task WaitUntilQueryableAsync_Queryable_Returns()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing(Ready(Definition)));

        // Act
        await _indexes.Object.WaitUntilQueryableAsync("default", TimeSpan.FromMinutes(1));

        // Assert
        _indexes.ListAsync("default", Any(), Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task WaitUntilQueryableAsync_NotQueryableWithinTheTimeout_ThrowsTimeoutException()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(() => new SearchIndexListing(Building()));

        // Act & Assert
        await Assert.That(() => _indexes.Object.WaitUntilQueryableAsync("default", TimeSpan.FromMilliseconds(20)))
            .ThrowsExactly<TimeoutException>();
    }

    [Test]
    public async Task WaitUntilQueryableAsync_CallerCanceled_ThrowsOperationCanceledException()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(() => new SearchIndexListing(Building()));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act & Assert
        await Assert.That(() => _indexes.Object.WaitUntilQueryableAsync("default", cancellationToken: canceled.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments("indexes")]
    [Arguments("name")]
    public async Task WaitUntilQueryableAsync_ArgumentMissing_ThrowsArgumentException(string argument)
    {
        // Arrange
        Func<Task> wait = argument == "indexes"
            ? () => ((IMongoSearchIndexManager)null!).WaitUntilQueryableAsync("default")
            : () => _indexes.Object.WaitUntilQueryableAsync("");

        // Act & Assert
        await Assert.That(wait).Throws<ArgumentException>();
    }

    private static BsonDocument Ready(BsonDocument definition) =>
        new() { { "name", "default" }, { "type", "search" }, { "status", "READY" }, { "queryable", true }, { "latestDefinition", definition } };

    private static BsonDocument Building() =>
        new() { { "name", "default" }, { "type", "search" }, { "status", "BUILDING" }, { "queryable", false } };
}
