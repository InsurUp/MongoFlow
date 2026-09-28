using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>How a keyed collection reads a document's key and finds a document by key.</summary>
internal sealed class KeyModel<TDocument, TKey>
{
    private readonly Func<TDocument, TKey> _get;
    private readonly Func<TKey, Expression<Func<TDocument, bool>>> _filter;

    private KeyModel(Func<TDocument, TKey> get,
        Func<TKey, Expression<Func<TDocument, bool>>> filter,
        CreateIndexModel<TDocument>? uniqueIndex)
    {
        _get = get;
        _filter = filter;
        UniqueIndex = uniqueIndex;
    }

    /// <summary>The unique index a custom key declares, or <see langword="null"/> for <c>_id</c> or when turned off.</summary>
    public CreateIndexModel<TDocument>? UniqueIndex { get; }

    public TKey Get(TDocument document) => _get(document);

    public Expression<Func<TDocument, bool>> Filter(TKey key) => _filter(key);

    public static KeyModel<TDocument, TKey> Create(Expression<Func<TDocument, TKey>>? key, bool unique, string collection)
    {
        if (key is null)
        {
            return FromId(collection);
        }

        return key.Body is NewExpression composite
            ? FromComposite(key, composite, unique, collection)
            : FromMember(key, unique);
    }

    private static KeyModel<TDocument, TKey> FromId(string collection)
    {
        var id = BsonClassMap.LookupClassMap(typeof(TDocument)).IdMemberMap
            ?? throw new VaultConfigurationException(
                $"{collection} is keyed by {typeof(TKey).Name}, but {typeof(TDocument).Name} has no member mapped to _id. " +
                "Configure a key, or declare the collection as keyless.");

        if (id.MemberType != typeof(TKey))
        {
            throw new VaultConfigurationException(
                $"{collection} is keyed by {typeof(TKey).Name}, but {typeof(TDocument).Name}.{id.MemberName}, its _id, " +
                $"is a {id.MemberType.Name}.");
        }

        var parameter = Expression.Parameter(typeof(TDocument), "x");
        var member = Expression.MakeMemberAccess(parameter, id.MemberInfo);
        var getter = Expression.Lambda<Func<TDocument, TKey>>(member, parameter).Compile();

        return new KeyModel<TDocument, TKey>(getter, value => Equals(member, value, parameter), uniqueIndex: null);
    }

    private static KeyModel<TDocument, TKey> FromMember(Expression<Func<TDocument, TKey>> key, bool unique)
    {
        var member = MemberExpressions.GetMember(key, nameof(key));
        var parameter = key.Parameters[0];

        return new KeyModel<TDocument, TKey>(
            key.Compile(),
            value => Equals(key.Body, value, parameter),
            unique ? UniqueIndexOn([member], parameter) : null);
    }

    private static KeyModel<TDocument, TKey> FromComposite(Expression<Func<TDocument, TKey>> key,
        NewExpression composite,
        bool unique,
        string collection)
    {
        var parameter = key.Parameters[0];
        var constructorParameters = composite.Constructor?.GetParameters() ?? [];
        var parts = new List<(Expression Member, Func<TKey, object?> Read)>();

        for (var i = 0; i < composite.Arguments.Count; i++)
        {
            var argument = composite.Arguments[i];
            var member = argument is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : argument;

            if (member is not MemberExpression { Expression: var owner } || owner != parameter || i >= constructorParameters.Length)
            {
                throw new VaultConfigurationException(
                    $"The key of {collection} must be a member, or a new {typeof(TKey).Name} of members such as " +
                    $"x => new {typeof(TKey).Name}(x.A, x.B), but it is {key}.");
            }

            var name = constructorParameters[i].Name!;
            var property = typeof(TKey).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new VaultConfigurationException(
                    $"The key of {collection} passes {member} as {name}, but {typeof(TKey).Name} has no property of that name.");

            var keyParameter = Expression.Parameter(typeof(TKey), "key");
            var read = Expression.Lambda<Func<TKey, object?>>(
                Expression.Convert(Expression.Property(keyParameter, property), typeof(object)),
                keyParameter).Compile();

            parts.Add((argument, read));
        }

        return new KeyModel<TDocument, TKey>(
            key.Compile(),
            value => Expression.Lambda<Func<TDocument, bool>>(
                parts
                    .Select(part => (Expression)Expression.Equal(part.Member, Expression.Constant(part.Read(value), part.Member.Type)))
                    .Aggregate(Expression.AndAlso),
                parameter),
            unique ? UniqueIndexOn(parts.Select(part => part.Member), parameter) : null);
    }

    private static Expression<Func<TDocument, bool>> Equals(Expression member, TKey value, ParameterExpression parameter) =>
        Expression.Lambda<Func<TDocument, bool>>(
            Expression.Equal(member, Expression.Constant(value, member.Type)),
            parameter);

    private static CreateIndexModel<TDocument> UniqueIndexOn(IEnumerable<Expression> members, ParameterExpression parameter)
    {
        var keys = members
            .Select(member => Builders<TDocument>.IndexKeys.Ascending(new ExpressionFieldDefinition<TDocument>(
                Expression.Lambda<Func<TDocument, object>>(Expression.Convert(member, typeof(object)), parameter))))
            .ToList();

        return new CreateIndexModel<TDocument>(
            keys.Count == 1 ? keys[0] : Builders<TDocument>.IndexKeys.Combine(keys),
            new CreateIndexOptions { Unique = true });
    }
}
