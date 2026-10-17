using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexSync"/> keeps a search index as declared:
/// <list type="number">
/// <item>one missing is created; one created meanwhile by another instance is compared like one listed;</item>
/// <item>one defined as declared is left, one defined otherwise is updated, and one of the other type fails;</item>
/// <item>waiting checks until the index is ready and queryable, every second at first, then less often, up to every five
/// seconds, saying once that it's building; an index that failed or is gone fails the wait.</item>
/// </list>
/// </summary>
public partial class SearchIndexSyncTests
{
    private static readonly BsonDocument Definition = BsonDocument.Parse("{ mappings: { dynamic: false, fields: { TenantId: { type: 'token' } } } }");

    private readonly Mock<IMongoSearchIndexManager> _indexes = IMongoSearchIndexManager.Mock();
    private readonly InstantTime _time = new();

    [Test]
    public async Task CreateOrUpdateAsync_NoneListed_CreatesIt()
    {
        // Act
        var change = await SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search, null, CancellationToken.None);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Created);
        _indexes.CreateOneAsync(Any<CreateSearchIndexModel>(), Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task CreateOrUpdateAsync_CreatedMeanwhile_ComparesWithTheOneCreated()
    {
        // Arrange — another instance created it between the listing and the create.
        _indexes.CreateOneAsync(Any<CreateSearchIndexModel>(), Any()).Throws(CommandFailed(68));
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing(Index("READY", Definition)));

        // Act
        var change = await SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search, null, CancellationToken.None);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Unchanged);
    }

    [Test]
    [Arguments(68)]
    [Arguments(26)]
    public async Task CreateOrUpdateAsync_CreateFailsAndNoneIsListed_ThrowsMongoCommandException(int code)
    {
        // Arrange
        _indexes.CreateOneAsync(Any<CreateSearchIndexModel>(), Any()).Throws(CommandFailed(code));
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing());

        // Act & Assert
        await Assert.That(() => SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search, null, CancellationToken.None))
            .ThrowsExactly<MongoCommandException>();
    }

    [Test]
    public async Task CreateOrUpdateAsync_ListedAsDeclared_LeavesIt()
    {
        // Act
        var change = await SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search,
            Index("READY", Definition), CancellationToken.None);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Unchanged);
        _indexes.UpdateAsync(Any(), Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task CreateOrUpdateAsync_ListedOtherwise_UpdatesIt()
    {
        // Arrange
        var listed = Index("READY", BsonDocument.Parse("{ mappings: { dynamic: true, fields: { } } }"));

        // Act
        var change = await SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search, listed, CancellationToken.None);

        // Assert
        await Assert.That(change).IsEqualTo(IndexChange.Updated);
        _indexes.UpdateAsync("default", Definition, Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task CreateOrUpdateAsync_ListedOfTheOtherType_ThrowsInvalidOperationException()
    {
        // Arrange
        var listed = Index("READY", Definition);
        listed["type"] = "vectorSearch";

        // Act & Assert
        await ThrowsTask(() => SearchIndexSync.CreateOrUpdateAsync(_indexes.Object, "default", Definition, SearchIndexType.Search, listed, CancellationToken.None))
            .IgnoreStackTrace();
    }

    [Test]
    public async Task WaitAsync_Queryable_ReturnsAtOnce()
    {
        // Arrange
        var building = new List<string>();
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing(Index("READY", Definition)));

        // Act
        await SearchIndexSync.WaitAsync(_indexes.Object, "default", _time, building.Add, CancellationToken.None);

        // Assert
        await Verify(new { _time.Waits, Building = building }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task WaitAsync_BuildingAWhile_ChecksLessOftenUpToEveryFiveSeconds()
    {
        // Arrange — an updated index stays queryable on its old version while it's READY again for the new one.
        var building = new List<string>();
        var updated = Index("READY", Definition);
        updated["queryable"] = false;
        _indexes.ListAsync(Any(), Any(), Any()).ReturnsSequentially(
            new SearchIndexListing(Index("PENDING", Definition)),
            new SearchIndexListing(Index("BUILDING", Definition)),
            new SearchIndexListing(Index("BUILDING", Definition)),
            new SearchIndexListing(updated),
            new SearchIndexListing(Index("BUILDING", Definition)),
            new SearchIndexListing(Index("READY", Definition)));

        // Act
        await SearchIndexSync.WaitAsync(_indexes.Object, "default", _time, building.Add, CancellationToken.None);

        // Assert
        await Verify(new { _time.Waits, Building = building });
    }

    [Test]
    [Arguments("a message")]
    [Arguments("details")]
    [Arguments("no reason")]
    public async Task WaitAsync_Failed_ThrowsInvalidOperationException(string reason)
    {
        // Arrange
        var failed = Index("FAILED", Definition);
        if (reason == "a message")
        {
            failed["message"] = "Field 'Name' can't be mapped as both string and token.";
        }
        else if (reason == "details")
        {
            failed["statusDetail"] = new BsonArray { new BsonDocument { { "hostname", "mongot-0" }, { "status", "FAILED" } } };
        }

        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing(failed));

        // Act & Assert
        await ThrowsTask(() => SearchIndexSync.WaitAsync(_indexes.Object, "default", _time, null, CancellationToken.None))
            .IgnoreStackTrace()
            .UseParameters(reason);
    }

    [Test]
    public async Task WaitAsync_Dropped_ThrowsInvalidOperationException()
    {
        // Arrange
        _indexes.ListAsync(Any(), Any(), Any()).Returns(new SearchIndexListing());

        // Act & Assert
        await ThrowsTask(() => SearchIndexSync.WaitAsync(_indexes.Object, "default", _time, null, CancellationToken.None))
            .IgnoreStackTrace();
    }

    private static BsonDocument Index(string status,
        BsonDocument definition) =>
        new()
        {
            { "id", "6700000000000000000000aa" },
            { "name", "default" },
            { "type", "search" },
            { "status", status },
            { "queryable", status == "READY" },
            { "latestDefinition", definition.DeepClone() }
        };
}
