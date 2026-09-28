using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

public static class SoftDeleteFeature
{
    /// <summary>The key of the built-in soft-delete feature, added with <c>UseSoftDelete</c>.</summary>
    public static FeatureKey Key { get; } = new("soft-delete");

    /// <summary>
    /// Adds the built-in soft-delete feature to every collection whose document is a
    /// <typeparamref name="TSoftDelete"/>: reads skip deleted documents, and a delete sets the flag instead of removing
    /// the document.
    /// </summary>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="isDeleted">
    /// A settable member with a typed parameter, such as <c>(ISoftDeleteEntity x) =&gt; x.IsDeleted</c>, so both type
    /// arguments are inferred.
    /// </param>
    public static IVaultBuilder<TVault> UseSoftDelete<TVault, TSoftDelete>(this IVaultBuilder<TVault> vault,
        Expression<Func<TSoftDelete, bool>> isDeleted)
        where TVault : MongoVault =>
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete, bool>(
            isDeleted,
            Negate(isDeleted),
            _ => true,
            nameof(isDeleted)));

    /// <summary>
    /// Adds the built-in soft-delete feature, recording when a document was deleted: reads skip documents with a
    /// timestamp, and a delete sets it to now from the <see cref="TimeProvider"/> in DI, or the system clock.
    /// </summary>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="deletedAt">A settable member with a typed parameter, such as <c>(IDeletedAt x) =&gt; x.DeletedAt</c>.</param>
    public static IVaultBuilder<TVault> UseSoftDelete<TVault, TSoftDelete>(this IVaultBuilder<TVault> vault,
        Expression<Func<TSoftDelete, DateTime?>> deletedAt)
        where TVault : MongoVault =>
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete, DateTime?>(
            deletedAt,
            IsNull(deletedAt),
            services => Clock(services).GetUtcNow().UtcDateTime,
            nameof(deletedAt)));

    /// <inheritdoc cref="UseSoftDelete{TVault, TSoftDelete}(IVaultBuilder{TVault}, Expression{Func{TSoftDelete, DateTime?}})"/>
    public static IVaultBuilder<TVault> UseSoftDelete<TVault, TSoftDelete>(this IVaultBuilder<TVault> vault,
        Expression<Func<TSoftDelete, DateTimeOffset?>> deletedAt)
        where TVault : MongoVault =>
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete, DateTimeOffset?>(
            deletedAt,
            IsNull(deletedAt),
            services => Clock(services).GetUtcNow(),
            nameof(deletedAt)));

    private static Expression<Func<T, bool>> Negate<T>(Expression<Func<T, bool>> member) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(member.Body), member.Parameters);

    private static Expression<Func<T, bool>> IsNull<T, TValue>(Expression<Func<T, TValue>> member) =>
        Expression.Lambda<Func<T, bool>>(
            Expression.Equal(member.Body, Expression.Constant(null, member.Body.Type)),
            member.Parameters);

    private static TimeProvider Clock(IServiceProvider services) =>
        services.GetService<TimeProvider>() ?? TimeProvider.System;
}

/// <summary>Soft delete over a member holding <typeparamref name="TValue"/>: a flag, or a deletion timestamp.</summary>
internal sealed class SoftDeleteFeature<TSoftDelete, TValue> : IVaultFeature, IVaultCollectionConfiguration
{
    private readonly Expression<Func<TSoftDelete, TValue>> _member;
    private readonly Expression<Func<TSoftDelete, bool>> _notDeleted;
    private readonly Func<IServiceProvider, TValue> _deletedValue;
    private readonly Action<TSoftDelete, TValue> _setMember;

    public SoftDeleteFeature(Expression<Func<TSoftDelete, TValue>> member,
        Expression<Func<TSoftDelete, bool>> notDeleted,
        Func<IServiceProvider, TValue> deletedValue,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(member, parameterName);

        _member = member;
        _notDeleted = notDeleted;
        _deletedValue = deletedValue;
        _setMember = MemberExpressions.CreateSetter(member, parameterName);
    }

    public static FeatureKey Key => SoftDeleteFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter(_notDeleted)
        .ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TSoftDelete)))
        {
            collection.AddInterceptor(
                new SoftDeleteInterceptor<TDocument, TSoftDelete, TValue>(_member, _setMember, _deletedValue));
        }
    }
}

/// <summary>Turns each delete into an update that marks the document deleted, and marks the deleted document too.</summary>
internal sealed class SoftDeleteInterceptor<TDocument, TSoftDelete, TValue>(
    Expression<Func<TSoftDelete, TValue>> member,
    Action<TSoftDelete, TValue> setMember,
    Func<IServiceProvider, TValue> deletedValue) : VaultInterceptor
{
    private readonly Expression<Func<TDocument, TValue>> _field = MemberExpressions.Rebind<TSoftDelete, TDocument, TValue>(member);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var deletes = context.Operations.OfType<DeleteOperation<TDocument>>().ToList();
        if (deletes.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var value = deletedValue(context.Services);
        var markDeleted = Builders<TDocument>.Update.Set(_field, value);

        foreach (var delete in deletes)
        {
            if (delete.Document is TSoftDelete document)
            {
                setMember(document, value);
            }

            context.Replace(delete, delete.ToUpdate(markDeleted));
        }

        return ValueTask.CompletedTask;
    }
}
