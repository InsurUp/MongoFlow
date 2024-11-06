using System.Linq.Expressions;

namespace MongoFlow;

internal static class KeyExpressionCache<TDocument>
{
    // ReSharper disable once InconsistentNaming
    private static Expression<Func<TDocument, object>>? Cache;

    public static Expression<Func<TDocument, object>> Get(DocumentProperty key)
    {
        if (Cache is not null)
        {
            return Cache;
        }

        var parameter = Expression.Parameter(typeof(TDocument), "x");
        var property = Expression.Property(parameter, key.Property);
        var convert = Expression.Convert(property, typeof(object));
        var lambda = Expression.Lambda<Func<TDocument, object>>(convert, parameter);

        return Cache = lambda;
    }
}
