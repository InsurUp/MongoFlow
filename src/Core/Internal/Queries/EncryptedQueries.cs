using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <summary>
/// LINQ queries of a collection with encrypted members, rewritten as they run. The driver would send the plain values a
/// filter compares encrypted members with, which match nothing the server holds: each <c>Where</c> on the documents
/// themselves that compares an encrypted member becomes a <c>$match</c> with those values encrypted. A query that uses an
/// encrypted member where no filter can be encrypted, such as a sort, or a filter after a projection, throws rather than
/// silently matching nothing.
/// </summary>
internal sealed class EncryptedQueries<TDocument>(CollectionModel<TDocument> model,
    ICollectionEncryption encryption)
{
    private static readonly MethodInfo AppendStageMethod =
        new Func<IQueryable<object>, PipelineStageDefinition<object, object>, IBsonSerializer<object>, IQueryable<object>>(
            MongoQueryable.AppendStage).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo WhereMethod =
        new Func<IQueryable<object>, Expression<Func<object, bool>>, IQueryable<object>>(Queryable.Where)
            .Method.GetGenericMethodDefinition();

    // The operators that keep the documents as they are, which a filter after can still be encrypted for.
    private static readonly HashSet<string> Keeping =
    [
        nameof(Queryable.Where), nameof(Queryable.OrderBy), nameof(Queryable.OrderByDescending), nameof(Queryable.ThenBy),
        nameof(Queryable.ThenByDescending), nameof(Queryable.Skip), nameof(Queryable.Take)
    ];

    // The operators whose predicate is a filter that can run as a Where before them: First(p) is First(Where(p)).
    private static readonly HashSet<string> WithPredicate =
    [
        nameof(Queryable.First), nameof(Queryable.FirstOrDefault), nameof(Queryable.Single), nameof(Queryable.SingleOrDefault),
        nameof(Queryable.Last), nameof(Queryable.LastOrDefault), nameof(Queryable.Any), nameof(Queryable.Count),
        nameof(Queryable.LongCount)
    ];

    // Element paths of member chains, such as d.Address.Street, or null for one that isn't encrypted; rendered once each.
    private readonly ConcurrentDictionary<string, string?> _paths = new();

    /// <summary>
    /// <paramref name="expression"/>, with the filters on the documents that compare encrypted members encrypted; the same
    /// instance when there are none.
    /// </summary>
    /// <exception cref="NotSupportedException">It uses an encrypted member where no filter can be encrypted.</exception>
    public Expression Rewrite(Expression expression)
    {
        var calls = new List<MethodCallExpression>();
        var source = WithWhere(expression);

        while (source is MethodCallExpression { Arguments.Count: > 0 } call && IsQueryOperator(call.Method))
        {
            calls.Add(call);
            source = call.Arguments[0];
        }

        if (source is not ConstantExpression || calls.Count == 0)
        {
            return expression;
        }

        var documents = true;
        var projected = false;
        var changed = false;

        // From the driver's query up.
        for (var i = calls.Count - 1; i >= 0; i--)
        {
            var call = calls[i];
            Expression rewritten = call.Update(call.Object, [source, .. call.Arguments.Skip(1)]);
            var name = call.Method.Name;

            if (documents && name == nameof(Queryable.Where) && Predicate(call) is { } predicate)
            {
                var filter = model.Render(predicate);
                if (FilterDocuments.Touches(filter, encryption.Paths))
                {
                    rewritten = Match(source, encryption.EncryptFilter(filter));
                    changed = true;
                }
            }
            else if (documents && Keeping.Contains(name))
            {
                ThrowIfReferenced(call, "sort by");
            }
            else
            {
                var projecting = name is nameof(Queryable.Select) or nameof(Queryable.SelectMany);
                if (projected && !projecting && call.Arguments.Skip(1).Any(IsLambda))
                {
                    throw new NotSupportedException(
                        $"A query of {model.Namespace.CollectionName} projects an encrypted member, then goes on with " +
                        $"{name}, which could compare it with plain values that match nothing the server holds. Filter " +
                        "and sort before projecting it.");
                }

                // Projected through as they are, encrypted members are read back decrypted; compared, they match nothing.
                var references = References(call);
                if (references.Compared.FirstOrDefault() is { } compared)
                {
                    throw Unsupported(compared, name);
                }

                if (references.Referenced.FirstOrDefault() is { } referenced)
                {
                    if (!projecting)
                    {
                        throw Unsupported(referenced, name);
                    }

                    projected = true;
                }

                documents = false;
            }

            source = rewritten;
        }

        return changed ? source : expression;
    }

    /// <summary>A terminal operator with a predicate, such as <c>First(p)</c>, as one after a <c>Where</c>.</summary>
    private static Expression WithWhere(Expression expression)
    {
        if (expression is not MethodCallExpression { Arguments: [var source, UnaryExpression { Operand: LambdaExpression predicate }] } call ||
            call.Method.DeclaringType != typeof(Queryable) || !WithPredicate.Contains(call.Method.Name) ||
            predicate.ReturnType != typeof(bool))
        {
            return expression;
        }

        var element = call.Method.GetGenericArguments()[0];
        var alone = typeof(Queryable).GetMethods()
            .Single(method => method.Name == call.Method.Name && method.GetParameters().Length == 1)
            .MakeGenericMethod(element);

        return Expression.Call(alone, Expression.Call(WhereMethod.MakeGenericMethod(element), source, Expression.Quote(predicate)));
    }

    private static bool IsLambda(Expression argument) =>
        argument is LambdaExpression or UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression };

    private static bool IsQueryOperator(MethodInfo method) =>
        method.DeclaringType == typeof(Queryable) || method.DeclaringType == typeof(MongoQueryable);

    /// <summary>The filter a <c>Where</c> on the documents themselves takes, or <see langword="null"/>.</summary>
    private static Expression<Func<TDocument, bool>>? Predicate(MethodCallExpression where) =>
        where.Arguments is [_, UnaryExpression { Operand: Expression<Func<TDocument, bool>> predicate }] ? predicate : null;

    private Expression Match(Expression source,
        BsonDocument filter)
    {
        var stage = new BsonDocumentPipelineStageDefinition<TDocument, TDocument>(new BsonDocument("$match", filter),
            model.MongoCollection.DocumentSerializer);

        return Expression.Call(AppendStageMethod.MakeGenericMethod(typeof(TDocument), typeof(TDocument)),
            source,
            Expression.Constant(stage, typeof(PipelineStageDefinition<TDocument, TDocument>)),
            Expression.Constant(model.MongoCollection.DocumentSerializer, typeof(IBsonSerializer<TDocument>)));
    }

    private void ThrowIfReferenced(MethodCallExpression call,
        string doing)
    {
        if (References(call).Referenced.FirstOrDefault() is { } path)
        {
            throw new NotSupportedException(
                $"{path} in {model.Namespace.CollectionName} is encrypted, so a query can't {doing} it: the server holds " +
                "it encrypted.");
        }
    }

    private NotSupportedException Unsupported(string path,
        string method) =>
        new($"{path} in {model.Namespace.CollectionName} is encrypted, so a query can't use it in {method}: only a Where on " +
            "the collection's documents, before any projection or join, can compare an encrypted member, through the " +
            "values MongoFlow encrypts. Use FindAsync, or a Where straight after QueryAsync.");

    private EncryptedReferences References(MethodCallExpression call)
    {
        var references = new EncryptedReferences(PathOf);
        foreach (var argument in call.Arguments.Skip(1))
        {
            references.Visit(argument);
        }

        return references;
    }

    /// <summary>
    /// The encrypted element path <paramref name="member"/> reaches, such as <c>d.Address.Street</c> reaching
    /// <c>Address</c>, when it's read from a document; otherwise <see langword="null"/>.
    /// </summary>
    private string? PathOf(MemberExpression member)
    {
        var names = new Stack<MemberInfo>();
        Expression? node = member;
        while (node is MemberExpression each)
        {
            names.Push(each.Member);
            node = each.Expression;
        }

        if (node is null || !node.Type.IsAssignableTo(typeof(TDocument)))
        {
            return null;
        }

        var chain = string.Join('.', names.Select(name => name.Name));
        return _paths.GetOrAdd(chain, _ => EncryptedPath(names.ToArray()));
    }

    // The longest prefix of the chain the driver can render as a field, such as d.Name of d.Name.Length.
    private string? EncryptedPath(MemberInfo[] members)
    {
        for (var length = members.Length; length > 0; length--)
        {
            var parameter = Expression.Parameter(typeof(TDocument), "d");
            Expression body = parameter;
            try
            {
                foreach (var member in members.Take(length))
                {
                    body = Expression.MakeMemberAccess(body.Type.IsAssignableTo(member.DeclaringType!)
                        ? body
                        : Expression.Convert(body, member.DeclaringType!), member);
                }

                var path = FilterDocuments.RenderField(Expression.Lambda(body, parameter), model.RenderArgs).FieldName;
                return encryption.Paths.FirstOrDefault(encrypted => ElementPaths.Reaches(path, encrypted)) is { } reached
                    ? reached
                    : null;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Not a field the driver renders, such as a string's Length: try the member holding it.
            }
        }

        return null;
    }

    /// <summary>The encrypted members a query operator's lambdas read, and those they compute with.</summary>
    private sealed class EncryptedReferences(Func<MemberExpression, string?> pathOf) : System.Linq.Expressions.ExpressionVisitor
    {
        public List<string> Referenced { get; } = [];

        public List<string> Compared { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (pathOf(node) is { } path)
            {
                Referenced.Add(path);
                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            Operand(node.Left);
            Operand(node.Right);

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object is { } instance)
            {
                Operand(instance);
            }

            foreach (var argument in node.Arguments)
            {
                Operand(argument);
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote))
            {
                Operand(node.Operand);
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            Operand(node.Test);

            return base.VisitConditional(node);
        }

        private void Operand(Expression operand)
        {
            while (operand is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            {
                operand = conversion.Operand;
            }

            if (operand is MemberExpression member && pathOf(member) is { } path)
            {
                Compared.Add(path);
            }
        }
    }
}
