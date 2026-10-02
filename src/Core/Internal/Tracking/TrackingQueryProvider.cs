using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>
/// The driver's LINQ provider, wrapping each query it creates so the query stays tracked, and tracking the document a
/// query executed for one returns, such as <c>FirstOrDefaultAsync</c>'s.
/// </summary>
internal sealed class TrackingQueryProvider<TDocument>(IMongoQueryProvider provider,
    IDocumentTracker<TDocument> tracker) : IMongoQueryProvider
{
    public IDocumentTracker<TDocument> Tracker => tracker;

    public BsonDocument[] LoggedStages => provider.LoggedStages;

    public IQueryable CreateQuery(Expression expression)
    {
        var query = provider.CreateQuery(expression);
        var tracked = typeof(TrackingQueryable<,>).MakeGenericType(typeof(TDocument), query.ElementType);

        return (IQueryable)Activator.CreateInstance(tracked, query, this)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new TrackingQueryable<TDocument, TElement>(provider.CreateQuery<TElement>(expression), this);

    // The driver doesn't implement it.
    public object? Execute(Expression expression) => provider.Execute(expression);

    public TResult Execute<TResult>(Expression expression) => Tracked(expression, provider.Execute<TResult>(expression));

    public async Task<TResult> ExecuteAsync<TResult>(Expression expression,
        CancellationToken cancellationToken = default) =>
        Tracked(expression, await provider.ExecuteAsync<TResult>(expression, cancellationToken));

    // Checked in this order, so a count isn't boxed to be checked.
    private TResult Tracked<TResult>(Expression expression,
        TResult result)
    {
        if (DocumentQueries.ReturnsDocuments(expression) && result is TDocument document)
        {
            tracker.Track(document);
        }

        return result;
    }
}
