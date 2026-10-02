using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>
/// The driver's LINQ provider, behind every query a vault collection returns. It rewrites the joins on other vault
/// queries the driver can't run (<see cref="VaultJoins"/>) and wraps each query it creates, so what's built on it does
/// too. When the read tracks changes, it tracks the document a query executed for one returns, such as
/// <c>FirstOrDefaultAsync</c>'s.
/// </summary>
/// <param name="database">The database of the collection queried, the only one a join can reach.</param>
/// <param name="tracker">The read's tracker, or <see langword="null"/> when it doesn't track changes.</param>
internal sealed class VaultQueryProvider<TDocument>(IMongoQueryProvider provider,
    DatabaseNamespace database,
    IDocumentTracker<TDocument>? tracker) : IMongoQueryProvider
{
    public IDocumentTracker<TDocument>? Tracker => tracker;

    public BsonDocument[] LoggedStages => provider.LoggedStages;

    public IQueryable CreateQuery(Expression expression)
    {
        var query = provider.CreateQuery(VaultJoins.Rewrite(expression, database));
        var wrapped = typeof(VaultQueryable<,>).MakeGenericType(typeof(TDocument), query.ElementType);

        return (IQueryable)Activator.CreateInstance(wrapped, query, this)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new VaultQueryable<TDocument, TElement>(provider.CreateQuery<TElement>(VaultJoins.Rewrite(expression, database)), this);

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
        if (tracker is not null && DocumentQueries.ReturnsDocuments(expression) && result is TDocument document)
        {
            tracker.Track(document);
        }

        return result;
    }
}
