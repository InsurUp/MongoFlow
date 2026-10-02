using MongoDB.Driver;

namespace MongoFlow;

/// <summary>The driver's cursor, tracking each batch as it arrives.</summary>
internal sealed class TrackingCursor<TDocument, TElement>(IAsyncCursor<TElement> cursor,
    IDocumentTracker<TDocument> tracker) : IAsyncCursor<TElement>
{
    public IEnumerable<TElement> Current => cursor.Current;

    public bool MoveNext(CancellationToken cancellationToken = default) => Tracked(cursor.MoveNext(cancellationToken));

    public async Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) =>
        Tracked(await cursor.MoveNextAsync(cancellationToken));

    public void Dispose() => cursor.Dispose();

    private bool Tracked(bool moved)
    {
        if (moved)
        {
            // A query tracks only when it returns documents, so its elements are documents.
            foreach (var element in cursor.Current)
            {
                tracker.Track((TDocument)(object)element!);
            }
        }

        return moved;
    }
}
