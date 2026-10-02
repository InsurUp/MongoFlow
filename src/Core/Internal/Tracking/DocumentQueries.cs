using System.Collections.Frozen;
using System.Linq.Expressions;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>Tells the LINQ queries that return a collection's documents as stored from those that reshape them.</summary>
internal static class DocumentQueries
{
    // The operators that filter, sort, page or pick documents, leaving each one whole.
    private static readonly FrozenSet<string> Keeping = FrozenSet.Create(StringComparer.Ordinal,
        nameof(Queryable.Where),
        nameof(Queryable.OrderBy),
        nameof(Queryable.OrderByDescending),
        nameof(Queryable.ThenBy),
        nameof(Queryable.ThenByDescending),
        nameof(Queryable.Skip),
        nameof(Queryable.Take),
        nameof(Queryable.Distinct),
        nameof(Queryable.OfType),
        nameof(Queryable.First),
        nameof(Queryable.FirstOrDefault),
        nameof(Queryable.Single),
        nameof(Queryable.SingleOrDefault),
        nameof(Queryable.Last),
        nameof(Queryable.LastOrDefault),
        nameof(Queryable.ElementAt),
        nameof(Queryable.ElementAtOrDefault));

    /// <summary>
    /// Whether <paramref name="expression"/>, a query on a collection, returns its documents: from the collection, only
    /// operators that keep documents whole. A projection, even to the document type, doesn't: a document missing fields
    /// could carry a default key.
    /// </summary>
    public static bool ReturnsDocuments(Expression expression)
    {
        // Each operator's source is its first argument, down to the collection.
        while (expression is MethodCallExpression { Method: var method } call)
        {
            var keeps = (method.DeclaringType == typeof(Queryable) && Keeping.Contains(method.Name)) ||
                        (method.DeclaringType == typeof(MongoQueryable) && method.Name == nameof(MongoQueryable.Sample));

            if (!keeps)
            {
                return false;
            }

            expression = call.Arguments[0];
        }

        return true;
    }
}
