using System.Collections;
using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// The driver's query, tracking the documents it returns, enumerated or through a cursor, as long as it returns documents
/// rather than a projection. The driver's async operators reach its cursor through <see cref="IAsyncCursorSource{T}"/>.
/// </summary>
internal sealed class TrackingQueryable<TDocument, TElement>(IQueryable<TElement> query,
    TrackingQueryProvider<TDocument> provider) : IOrderedQueryable<TElement>, IAsyncCursorSource<TElement>
{
    public Type ElementType => query.ElementType;

    public Expression Expression => query.Expression;

    public IQueryProvider Provider => provider;

    public IEnumerator<TElement> GetEnumerator() =>
        DocumentQueries.ReturnsDocuments(query.Expression) ? Tracked(query.GetEnumerator()) : query.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncCursor<TElement> ToCursor(CancellationToken cancellationToken = default) =>
        Tracked(((IAsyncCursorSource<TElement>)query).ToCursor(cancellationToken));

    public async Task<IAsyncCursor<TElement>> ToCursorAsync(CancellationToken cancellationToken = default) =>
        Tracked(await ((IAsyncCursorSource<TElement>)query).ToCursorAsync(cancellationToken));

    public override string? ToString() => query.ToString();

    private IAsyncCursor<TElement> Tracked(IAsyncCursor<TElement> cursor) =>
        DocumentQueries.ReturnsDocuments(query.Expression)
            ? new TrackingCursor<TDocument, TElement>(cursor, provider.Tracker)
            : cursor;

    private IEnumerator<TElement> Tracked(IEnumerator<TElement> elements)
    {
        using (elements)
        {
            while (elements.MoveNext())
            {
                provider.Tracker.Track((TDocument)(object)elements.Current!);
                yield return elements.Current;
            }
        }
    }
}
