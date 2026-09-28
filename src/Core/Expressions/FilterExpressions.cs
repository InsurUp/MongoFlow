using System.Linq.Expressions;

namespace MongoFlow;

internal static class FilterExpressions
{
    public static Expression<Func<T, bool>> MatchNothing<T>() => _ => false;

    /// <summary>
    /// Joins filters with AND onto one parameter. Filters that are the constant <see langword="true"/> are dropped; one
    /// that is the constant <see langword="false"/> makes the result match nothing. Returns <see langword="null"/> when
    /// nothing is left to filter by.
    /// </summary>
    public static Expression<Func<T, bool>>? Combine<T>(IEnumerable<Expression<Func<T, bool>>?> filters)
    {
        ParameterExpression? parameter = null;
        Expression? body = null;

        foreach (var filter in filters)
        {
            switch (filter?.Body)
            {
                case null:
                case ConstantExpression { Value: true }:
                    continue;

                case ConstantExpression { Value: false }:
                    return MatchNothing<T>();
            }

            parameter ??= Expression.Parameter(typeof(T), "x");
            var next = new ParameterReplacer(filter.Parameters[0], parameter).Visit(filter.Body);

            body = body is null ? next : Expression.AndAlso(body, next);
        }

        return body is null ? null : Expression.Lambda<Func<T, bool>>(body, parameter!);
    }

    public static Expression<Func<T, bool>>? Combine<T>(params Expression<Func<T, bool>>?[] filters) =>
        Combine((IEnumerable<Expression<Func<T, bool>>?>)filters);
}
