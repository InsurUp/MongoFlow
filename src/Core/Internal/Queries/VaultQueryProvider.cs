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
/// <remarks>
/// On a collection with encrypted members, each query is rewritten as it runs, so the values its filters compare them
/// with are encrypted (<see cref="EncryptedQueries{TDocument}"/>).
/// </remarks>
/// <param name="model">The collection queried, whose database is the only one a join can reach.</param>
/// <param name="tracker">The read's tracker, or <see langword="null"/> when it doesn't track changes.</param>
internal sealed class VaultQueryProvider<TDocument>(IMongoQueryProvider provider,
    CollectionModel<TDocument> model,
    IDocumentTracker<TDocument>? tracker) : IMongoQueryProvider
{
    private readonly DatabaseNamespace _database = model.Namespace.DatabaseNamespace;

    public IDocumentTracker<TDocument>? Tracker => tracker;

    public BsonDocument[] LoggedStages => provider.LoggedStages;

    public IQueryable CreateQuery(Expression expression)
    {
        var query = provider.CreateQuery(VaultJoins.Rewrite(expression, _database));
        var wrapped = typeof(VaultQueryable<,>).MakeGenericType(typeof(TDocument), query.ElementType);

        return (IQueryable)Activator.CreateInstance(wrapped, query, this)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
        new VaultQueryable<TDocument, TElement>(provider.CreateQuery<TElement>(VaultJoins.Rewrite(expression, _database)), this);

    // The driver doesn't implement it.
    public object? Execute(Expression expression) => provider.Execute(expression);

    public TResult Execute<TResult>(Expression expression) =>
        Tracked(expression, provider.Execute<TResult>(Encrypted(expression)));

    public async Task<TResult> ExecuteAsync<TResult>(Expression expression,
        CancellationToken cancellationToken = default) =>
        Tracked(expression, await provider.ExecuteAsync<TResult>(Encrypted(expression), cancellationToken));

    /// <summary><paramref name="query"/> as it runs: the driver's query of its expression with encrypted filters, if it has any.</summary>
    public IQueryable<TElement> Encrypted<TElement>(IQueryable<TElement> query)
    {
        var expression = Encrypted(query.Expression);

        return ReferenceEquals(expression, query.Expression) ? query : provider.CreateQuery<TElement>(expression);
    }

    private Expression Encrypted(Expression expression) => model.EncryptedQueries?.Rewrite(expression) ?? expression;

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
