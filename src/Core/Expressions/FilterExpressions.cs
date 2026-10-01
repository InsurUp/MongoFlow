using System.Linq.Expressions;

namespace MongoFlow;

internal static class FilterExpressions
{
    /// <summary>
    /// Joins filters with AND, onto the first filter's parameter. Filters that are null or the constant
    /// <see langword="true"/> are dropped, and one that is the constant <see langword="false"/> is the result. Returns
    /// <see langword="null"/> when nothing is left to filter by, and the one filter left as it is.
    /// </summary>
    public static Expression<Func<T, bool>>? Combine<T>(params ReadOnlySpan<Expression<Func<T, bool>>?> filters)
    {
        Expression<Func<T, bool>>? first = null;
        Expression? body = null;

        foreach (var filter in filters)
        {
            switch (filter?.Body)
            {
                case null:
                case ConstantExpression { Value: true }:
                    continue;

                case ConstantExpression { Value: false }:
                    return filter;
            }

            if (first is null)
            {
                first = filter;
                continue;
            }

            body = Expression.AndAlso(body ?? first.Body, ParameterReplacer.Inline(filter, first.Parameters[0]));
        }

        return body is null ? first : Expression.Lambda<Func<T, bool>>(body, first!.Parameters[0]);
    }
}
