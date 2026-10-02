using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>
/// Joins on the queries vault collections return. The driver joins a collection only as a whole, so a join on a
/// vault query, which carries its collection's query filters, would fail. Rewritten as the driver's <c>Lookup</c>, the
/// query's filters run inside the <c>$lookup</c>, on the documents each outer document matches.
/// </summary>
/// <remarks>
/// Only filters go into the <c>$lookup</c>: there, a page or a sort would apply to each outer document's matches rather
/// than to the whole collection, so a join on a query with one is left to the driver, which rejects it.
/// </remarks>
internal static class VaultJoins
{
    // The collection each vault query reads, by the driver's query it starts from; weak, so it keeps no query alive.
    private static readonly ConditionalWeakTable<IQueryable, ICollectionModel> Collections = new();

    private static readonly MethodInfo LookupMethod = new Func<IQueryable<object>, IMongoCollection<object>,
        Expression<Func<object, object>>, Expression<Func<object, object>>,
        Expression<Func<object, IQueryable<object>, IQueryable<object>>>, IQueryable<LookupResult<object, object>>>(
        MongoQueryable.Lookup).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo WhereMethod =
        new Func<IQueryable<object>, Expression<Func<object, bool>>, IQueryable<object>>(Queryable.Where)
            .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo SelectMethod =
        new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<object>>(Queryable.Select)
            .Method.GetGenericMethodDefinition();

    private static readonly MethodInfo SelectManyMethod = new Func<IQueryable<object>,
        Expression<Func<object, IEnumerable<object>>>, Expression<Func<object, object, object>>, IQueryable<object>>(
        Queryable.SelectMany).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo DefaultIfEmptyMethod =
        new Func<IEnumerable<object>, IEnumerable<object?>>(Enumerable.DefaultIfEmpty).Method.GetGenericMethodDefinition();

    /// <summary>
    /// <paramref name="query"/>, a driver's query vault queries start from, remembered as reading <paramref name="model"/>.
    /// </summary>
    public static IQueryable<TDocument> Registered<TDocument>(IQueryable<TDocument> query,
        ICollectionModel model)
    {
        Collections.Add(query, model);

        return query;
    }

    /// <summary>
    /// <paramref name="expression"/>, or the driver's <c>Lookup</c> in its place when it's a join on a vault query with
    /// filters. A join on a vault query of another database than <paramref name="database"/> throws: a <c>$lookup</c>
    /// would look in the outer query's database, and find nothing.
    /// </summary>
    public static Expression Rewrite(Expression expression,
        DatabaseNamespace database)
    {
        if (expression is not MethodCallExpression { Arguments: [_, var inner, _, _, _] } join || !IsJoin(join.Method))
        {
            return expression;
        }

        // The filters on the inner query, innermost first, down to the driver's query it starts from.
        var filters = new Stack<LambdaExpression>();
        while (inner is MethodCallExpression
               {
                   Method: { Name: nameof(Queryable.Where) } where,
                   Arguments: [var source, UnaryExpression { Operand: LambdaExpression { Parameters.Count: 1 } filter }]
               } && where.DeclaringType == typeof(Queryable))
        {
            filters.Push(filter);
            inner = source;
        }

        if (inner is not ConstantExpression { Value: IQueryable query } ||
            !Collections.TryGetValue(query, out var collection))
        {
            return expression;
        }

        if (!collection.Namespace.DatabaseNamespace.Equals(database))
        {
            throw new InvalidOperationException(
                $"A query in the {database.DatabaseName} database can't be joined with one on " +
                $"{collection.Namespace.FullName}: a $lookup only reaches collections of its own database.");
        }

        // Without filters, it's the whole collection, which the driver joins itself.
        return filters.Count == 0 ? expression : Lookup(join, collection.MongoCollectionConstant, filters);
    }

    private static bool IsJoin(MethodInfo method) =>
        (method.DeclaringType == typeof(Queryable) || method.DeclaringType == typeof(MongoQueryable)) &&
        method.Name is nameof(Queryable.Join) or nameof(Queryable.GroupJoin) or nameof(Queryable.LeftJoin);

    /// <summary>
    /// The driver's <c>Lookup</c> of each outer document's matches in <paramref name="collection"/>, through
    /// <paramref name="filters"/>, shaped as <paramref name="join"/> shapes its results: one per outer document for a
    /// group join, one per match for a join, and one per outer document without matches too for a left join.
    /// </summary>
    private static Expression Lookup(MethodCallExpression join,
        ConstantExpression collection,
        IEnumerable<LambdaExpression> filters)
    {
        // TOuter, TInner, TKey, TResult
        var types = join.Method.GetGenericArguments();
        var (outerType, innerType, resultType) = (types[0], types[1], types[3]);

        var local = Expression.Parameter(outerType, "local");
        var foreign = Expression.Parameter(typeof(IQueryable<>).MakeGenericType(innerType), "foreign");
        var where = WhereMethod.MakeGenericMethod(innerType);
        var pipeline = filters.Aggregate<LambdaExpression, Expression>(foreign,
            (source, filter) => Expression.Call(where, source, Expression.Quote(filter)));

        var lookup = Expression.Call(LookupMethod.MakeGenericMethod(outerType, innerType, types[2], innerType),
            join.Arguments[0],
            collection,
            join.Arguments[2],
            join.Arguments[3],
            Expression.Quote(Expression.Lambda(pipeline, local, foreign)));

        var row = Expression.Parameter(typeof(LookupResult<,>).MakeGenericType(outerType, innerType), "row");
        var outer = Expression.Property(row, nameof(LookupResult<object, object>.Local));
        var matches = Expression.Property(row, nameof(LookupResult<object, object>.Results));
        var selector = (LambdaExpression)((UnaryExpression)join.Arguments[4]).Operand;

        if (join.Method.Name == nameof(Queryable.GroupJoin))
        {
            var grouped = Expression.Lambda(typeof(Func<,>).MakeGenericType(row.Type, resultType),
                ParameterReplacer.Inline(selector, outer, matches),
                row);

            return Expression.Call(SelectMethod.MakeGenericMethod(row.Type, resultType), lookup, Expression.Quote(grouped));
        }

        Expression each = join.Method.Name == nameof(Queryable.LeftJoin)
            ? Expression.Call(DefaultIfEmptyMethod.MakeGenericMethod(innerType), matches)
            : matches;
        var match = Expression.Parameter(innerType, "match");

        var collectionSelector = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(row.Type, typeof(IEnumerable<>).MakeGenericType(innerType)),
            each,
            row);
        var resultSelector = Expression.Lambda(typeof(Func<,,>).MakeGenericType(row.Type, innerType, resultType),
            ParameterReplacer.Inline(selector, outer, match),
            row,
            match);

        return Expression.Call(SelectManyMethod.MakeGenericMethod(row.Type, innerType, resultType),
            lookup,
            Expression.Quote(collectionSelector),
            Expression.Quote(resultSelector));
    }
}
