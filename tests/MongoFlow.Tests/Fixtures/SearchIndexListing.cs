using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>Stands in for what <c>$listSearchIndexes</c> returns: the indexes given, in one batch.</summary>
public sealed class SearchIndexListing(params BsonDocument[] indexes) : IAsyncCursor<BsonDocument>
{
    private bool _moved;

    public IEnumerable<BsonDocument> Current => indexes;

    public bool MoveNext(CancellationToken cancellationToken = default)
    {
        var moved = !_moved;
        _moved = true;
        return moved;
    }

    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(MoveNext(cancellationToken));

    public void Dispose()
    {
    }
}
