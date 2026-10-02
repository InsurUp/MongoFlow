using System.Collections;
using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// The driver's query, as a vault collection returns it: what's built on it goes through
/// <see cref="VaultQueryProvider{TDocument}"/>, and when the read tracks changes, the documents it returns, enumerated or
/// through a cursor, are tracked as long as it returns documents rather than a projection. The driver's async operators
/// reach its cursor through <see cref="IAsyncCursorSource{T}"/>.
/// </summary>
internal sealed class VaultQueryable<TDocument, TElement>(IQueryable<TElement> query,
    VaultQueryProvider<TDocument> provider) : IOrderedQueryable<TElement>, IAsyncCursorSource<TElement>
{
    public Type ElementType => query.ElementType;

    public Expression Expression => query.Expression;

    public IQueryProvider Provider => provider;

    public IEnumerator<TElement> GetEnumerator() =>
        TrackerOfDocuments() is { } tracker ? Tracked(query.GetEnumerator(), tracker) : query.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncCursor<TElement> ToCursor(CancellationToken cancellationToken = default) =>
        Tracked(((IAsyncCursorSource<TElement>)query).ToCursor(cancellationToken));

    public async Task<IAsyncCursor<TElement>> ToCursorAsync(CancellationToken cancellationToken = default) =>
        Tracked(await ((IAsyncCursorSource<TElement>)query).ToCursorAsync(cancellationToken));

    public override string? ToString() => query.ToString();

    /// <summary>The read's tracker, when it tracks changes and this query returns documents.</summary>
    private IDocumentTracker<TDocument>? TrackerOfDocuments() =>
        provider.Tracker is { } tracker && DocumentQueries.ReturnsDocuments(query.Expression) ? tracker : null;

    private IAsyncCursor<TElement> Tracked(IAsyncCursor<TElement> cursor) =>
        TrackerOfDocuments() is { } tracker ? new TrackingCursor<TDocument, TElement>(cursor, tracker) : cursor;

    private static IEnumerator<TElement> Tracked(IEnumerator<TElement> elements,
        IDocumentTracker<TDocument> tracker)
    {
        using (elements)
        {
            while (elements.MoveNext())
            {
                tracker.Track((TDocument)(object)elements.Current!);
                yield return elements.Current;
            }
        }
    }
}
