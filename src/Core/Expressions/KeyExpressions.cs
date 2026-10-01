using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization;

namespace MongoFlow;

/// <summary>Builds how a keyed collection finds a document by key.</summary>
internal static class KeyExpressions
{
    /// <summary>The member the driver maps to <c>_id</c>, as the key <c>x =&gt; x.Id</c>.</summary>
    /// <exception cref="VaultConfigurationException">There's no such member, or it isn't a <typeparamref name="TKey"/>.</exception>
    public static Expression<Func<TDocument, TKey>> Id<TDocument, TKey>(string collection)
    {
        var id = BsonClassMap.LookupClassMap(typeof(TDocument)).IdMemberMap
            ?? throw new VaultConfigurationException(
                $"{collection} is keyed by {typeof(TKey).Name}, but {typeof(TDocument).Name} has no member mapped to _id. " +
                "Configure a key, or declare the collection as keyless.");

        if (id.MemberType != typeof(TKey))
        {
            throw new VaultConfigurationException(
                $"{collection} is keyed by {typeof(TKey).Name}, but {typeof(TDocument).Name}.{id.MemberName}, its _id, " +
                $"is of type {id.MemberType.Name}.");
        }

        var parameter = Expression.Parameter(typeof(TDocument), "x");

        return Expression.Lambda<Func<TDocument, TKey>>(Expression.MakeMemberAccess(parameter, id.MemberInfo), parameter);
    }

    /// <summary>Finds a document by a key that is a member, such as <c>p =&gt; p.PolicyNumber</c>.</summary>
    /// <exception cref="ArgumentException">The key isn't a member of the parameter.</exception>
    public static Func<TKey, Expression<Func<TDocument, bool>>> MemberFilter<TDocument, TKey>(
        Expression<Func<TDocument, TKey>> key)
    {
        key.GetMember(nameof(key)); // throws unless the key is a member
        var parameter = key.Parameters[0];

        return value => Expression.Lambda<Func<TDocument, bool>>(Equal(key.Body, value), parameter);
    }

    /// <summary>
    /// Finds a document by a <typeparamref name="TKey"/> built from members, such as
    /// <c>t =&gt; new TokenKey(t.UserId, t.Provider)</c>. Each constructor argument is matched to the key's property named
    /// like its parameter, so a lookup compares every member with its part of the key.
    /// </summary>
    /// <exception cref="VaultConfigurationException">
    /// An argument isn't a member of the parameter, or the key has no property named like its constructor parameter that
    /// the argument's type can hold.
    /// </exception>
    public static Func<TKey, Expression<Func<TDocument, bool>>> CompositeFilter<TDocument, TKey>(
        Expression<Func<TDocument, TKey>> key,
        NewExpression composite,
        string collection)
    {
        if (composite is not { Constructor: { } constructor, Arguments.Count: > 0 })
        {
            throw NotComposite(key, collection);
        }

        var parameter = key.Parameters[0];
        var constructorParameters = constructor.GetParameters();
        var parts = new (Expression Member, Func<TKey, object?> Read)[composite.Arguments.Count];

        for (var i = 0; i < parts.Length; i++)
        {
            var argument = composite.Arguments[i];
            var member = argument.AsMemberOf(parameter) ?? throw NotComposite(key, collection);

            var name = constructorParameters[i].Name!;
            var property = typeof(TKey).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new VaultConfigurationException(
                    $"The key of {collection} passes {member} as {name}, but {typeof(TKey).Name} has no property of that name.");

            if (!argument.Type.IsAssignableFrom(property.PropertyType))
            {
                throw new VaultConfigurationException(
                    $"The key of {collection} passes {member} as {name}, of type {argument.Type.Name}, but " +
                    $"{typeof(TKey).Name}.{property.Name} is of type {property.PropertyType.Name}.");
            }

            parts[i] = (argument, CompileReader<TKey>(property));
        }

        return value =>
        {
            var body = Equal(parts[0].Member, parts[0].Read(value));

            for (var i = 1; i < parts.Length; i++)
            {
                body = Expression.AndAlso(body, Equal(parts[i].Member, parts[i].Read(value)));
            }

            return Expression.Lambda<Func<TDocument, bool>>(body, parameter);
        };
    }

    /// <summary><c>member == value</c>, with the value typed like the member.</summary>
    private static BinaryExpression Equal(Expression member,
        object? value) =>
        Expression.Equal(member, Expression.Constant(value, member.Type));

    /// <summary>Compiles <c>key =&gt; (object)key.Property</c>.</summary>
    private static Func<TKey, object?> CompileReader<TKey>(PropertyInfo property)
    {
        var key = Expression.Parameter(typeof(TKey), "key");

        return Expression.Lambda<Func<TKey, object?>>(
            Expression.Convert(Expression.Property(key, property), typeof(object)),
            key).Compile();
    }

    private static VaultConfigurationException NotComposite<TDocument, TKey>(Expression<Func<TDocument, TKey>> key,
        string collection) =>
        new($"The key of {collection} must be a member, or a new {typeof(TKey).Name} of members such as " +
            $"x => new {typeof(TKey).Name}(x.A, x.B), but it is {key}.");
}
