using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization;

namespace MongoFlow;

/// <summary>Builds how a keyed collection reads a document's key, and which members a key matches.</summary>
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

    /// <summary>A key that is a member, such as <c>p =&gt; p.PolicyNumber</c>, matched as it is.</summary>
    /// <exception cref="ArgumentException">The key isn't a member of the parameter.</exception>
    /// <exception cref="VaultConfigurationException">The key converts the member to another type.</exception>
    public static (LambdaExpression Member, Func<TKey, object?> Read) MemberPart<TDocument, TKey>(
        Expression<Func<TDocument, TKey>> key,
        string collection)
    {
        var member = key.GetMember(nameof(key));

        if (member != key.Body)
        {
            throw new VaultConfigurationException(
                $"The key of {collection} converts {member} to {typeof(TKey).Name}. Key it by a member of type {typeof(TKey).Name}.");
        }

        return (key, value => value);
    }

    /// <summary>
    /// The parts of a <typeparamref name="TKey"/> built from members, such as
    /// <c>t =&gt; new TokenKey(t.UserId, t.Provider)</c>. Each constructor argument is matched to the key's property named
    /// like its parameter, so a lookup matches every member with its part of the key.
    /// </summary>
    /// <exception cref="VaultConfigurationException">
    /// An argument isn't a member of the parameter, or the key has no property of the member's type named like its
    /// constructor parameter.
    /// </exception>
    public static (LambdaExpression Member, Func<TKey, object?> Read)[] CompositeParts<TDocument, TKey>(
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
        var parts = new (LambdaExpression Member, Func<TKey, object?> Read)[composite.Arguments.Count];

        for (var i = 0; i < parts.Length; i++)
        {
            var argument = composite.Arguments[i];
            var member = argument.AsMemberOf(parameter) ?? throw NotComposite(key, collection);

            var name = constructorParameters[i].Name!;
            var property = typeof(TKey).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new VaultConfigurationException(
                    $"The key of {collection} passes {member} as {name}, but {typeof(TKey).Name} has no property of that name.");

            if (argument != member)
            {
                throw new VaultConfigurationException(
                    $"The key of {collection} converts {member} to {argument.Type.Name} to pass it as {name}. Pass a member " +
                    "of the parameter's type.");
            }

            if (property.PropertyType != member.Type)
            {
                throw new VaultConfigurationException(
                    $"The key of {collection} passes {member}, of type {member.Type.Name}, as {name}, but " +
                    $"{typeof(TKey).Name}.{property.Name} is of type {property.PropertyType.Name}.");
            }

            parts[i] = (Expression.Lambda(member, parameter), CompileReader<TKey>(property));
        }

        return parts;
    }

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
