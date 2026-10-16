using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="CollectionSearch"/> knows what a collection's search indexes map:
/// <list type="number">
/// <item>a declared index from its declaration, without asking the server;</item>
/// <item>any other from the server, listed once for every search, and kept;</item>
/// <item>a listing that fails, or finds no index, is tried again by the next search; a search canceled while waiting
/// leaves the listing to the others;</item>
/// <item>an index of the other type than the search's fails it;</item>
/// <item>the fields a search matches after it are warned about once each.</item>
/// </list>
/// </summary>
public partial class CollectionSearchTests
{
    private const string Collection = "Products";

    private static readonly BsonDocument Listed = BsonDocument.Parse("""
        { name: 'default', type: 'search', status: 'READY', queryable: true,
          latestDefinition: { mappings: { dynamic: false, fields: { TenantId: { type: 'token' } } } } }
        """);

    private readonly LogSink _log = new();

    [Test]
    public async Task MappingAsync_DeclaredIndex_IsNotListed()
    {
        // Arrange
        var lister = new Lister(Listed);
        var declared = new SearchIndexDeclaration("default", SearchIndexType.Search,
            BsonDocument.Parse("{ mappings: { dynamic: false, fields: { Name: { type: 'token' } } } }"));
        var search = Search([declared], lister);

        // Act
        var mapping = await search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None);

        // Assert
        await Verify(new { NameEquals = mapping.CanEqual("Name", "ada"), lister.Calls });
    }

    [Test]
    public async Task MappingAsync_UndeclaredIndexSearchedAtOnce_IsListedOnce()
    {
        // Arrange
        var lister = new Lister(Listed) { Gate = new TaskCompletionSource() };
        var search = Search([], lister);

        // Act
        var first = search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None).AsTask();
        var second = search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None).AsTask();
        lister.Gate.SetResult();
        await Task.WhenAll(first, second);
        var mapping = await search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None);

        // Assert
        await Verify(new { TenantEquals = mapping.CanEqual("TenantId", "t-1"), lister.Calls, Log = _log.Snapshot() });
    }

    [Test]
    public async Task MappingAsync_ListingFailed_IsListedAgain()
    {
        // Arrange
        var lister = new Lister(Listed) { FailFirst = new MongoException("The server can't be reached.") };
        var search = Search([], lister);
        await Assert.That(() => (Task)search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None).AsTask())
            .ThrowsExactly<MongoException>();

        // Act
        await search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None);

        // Assert
        await Assert.That(lister.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task MappingAsync_NoSuchIndex_ThrowsInvalidOperationException()
    {
        // Arrange
        var search = Search([], new Lister(null));

        // Act & Assert
        await ThrowsTask(() => search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None).AsTask())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task MappingAsync_ListedWithoutADefinition_MapsNothing()
    {
        // Arrange
        var search = Search([], new Lister(new BsonDocument { { "name", "default" }, { "type", "search" } }));

        // Act
        var mapping = await search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None);

        // Assert
        await Assert.That(mapping.CanEqual("TenantId", "t-1")).IsFalse();
    }

    [Test]
    public async Task MappingAsync_SearchCanceledWhileListing_LeavesTheListingToOthers()
    {
        // Arrange
        var lister = new Lister(Listed) { Gate = new TaskCompletionSource() };
        var search = Search([], lister);
        using var canceled = new CancellationTokenSource();
        var waiting = search.MappingAsync("default", SearchIndexType.Search, canceled.Token).AsTask();
        var other = search.MappingAsync("default", SearchIndexType.Search, CancellationToken.None).AsTask();

        // Act
        await canceled.CancelAsync();
        lister.Gate.SetResult();

        // Assert
        await Assert.That(() => (Task)waiting).Throws<OperationCanceledException>();
        await Assert.That((await other).CanEqual("TenantId", "t-1")).IsTrue();
        await Assert.That(lister.Calls).IsEqualTo(1);
    }

    [Test]
    [Arguments("search index searched for vectors")]
    [Arguments("vector search index searched as text")]
    public async Task MappingAsync_IndexOfTheOtherType_ThrowsInvalidOperationException(string index)
    {
        // Arrange
        var vectors = new SearchIndexDeclaration("vectors", SearchIndexType.VectorSearch,
            BsonDocument.Parse("{ fields: [{ type: 'vector', path: 'Embedding', numDimensions: 3, similarity: 'cosine' }] }"));
        var search = Search([vectors], new Lister(Listed));

        // Act & Assert
        await ThrowsTask(() => (index.StartsWith("search")
                ? search.MappingAsync("default", SearchIndexType.VectorSearch, CancellationToken.None)
                : search.MappingAsync("vectors", SearchIndexType.Search, CancellationToken.None)).AsTask())
            .IgnoreStackTrace()
            .UseParameters(index);
    }

    [Test]
    public async Task NotePostFiltered_FieldsOfSeveralSearches_WarnsOnceEach()
    {
        // Arrange
        var search = Search([], new Lister(Listed));

        // Act
        search.NotePostFiltered("default", ["Price", "Name"]);
        search.NotePostFiltered("default", ["Price"]);
        search.NotePostFiltered("vectors", ["Price"]);

        // Assert
        await Verify(_log.Snapshot());
    }

    private CollectionSearch Search(IReadOnlyList<SearchIndexDeclaration> declarations,
        Lister lister) =>
        new(Collection, declarations, lister.ListAsync, _log.CreateLogger(MongoFlowLogEvents.Search.Category));
}
