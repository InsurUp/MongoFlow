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

    /// <summary>
    /// <paramref name="filter"/> as a filter of <typeparamref name="TDocument"/>, when it takes one parameter a
    /// <typeparamref name="TDocument"/> can be passed as, such as an interface it implements or <see cref="object"/>,
    /// and returns <see cref="bool"/>; otherwise <see langword="null"/>.
    /// </summary>
    public static Expression<Func<TDocument, bool>>? ForDocument<TDocument>(LambdaExpression filter) =>
        filter is { Parameters: [var parameter] } && filter.ReturnType == typeof(bool) &&
        typeof(TDocument).IsAssignableTo(parameter.Type)
            ? MemberExpressions.Rebind<TDocument, bool>(filter)
            : null;

    /// <summary>
    /// Why <paramref name="filter"/>, which <see cref="ForDocument"/> refused, can't filter
    /// <typeparamref name="TDocument"/>.
    /// </summary>
    public static string Unusable<TDocument>(LambdaExpression filter) =>
        $"{filter} can't filter {typeof(TDocument).Name}: a query filter takes one parameter of type " +
        $"{typeof(TDocument).Name}, a type it derives from or implements, or object, and returns bool.";
}
