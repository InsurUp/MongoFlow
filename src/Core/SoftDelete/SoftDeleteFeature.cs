using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

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
            Negate(NotNull(isDeleted, nameof(isDeleted))),
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
            IsNull(NotNull(deletedAt, nameof(deletedAt))),
            services => Clock(services).GetUtcNow().UtcDateTime,
            nameof(deletedAt)));

    /// <inheritdoc cref="UseSoftDelete{TVault, TSoftDelete}(IVaultBuilder{TVault}, Expression{Func{TSoftDelete, DateTime?}})"/>
    public static IVaultBuilder<TVault> UseSoftDelete<TVault, TSoftDelete>(this IVaultBuilder<TVault> vault,
        Expression<Func<TSoftDelete, DateTimeOffset?>> deletedAt)
        where TVault : MongoVault =>
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete, DateTimeOffset?>(
            deletedAt,
            IsNull(NotNull(deletedAt, nameof(deletedAt))),
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

    // The filter is built from the member before the feature checks it, so it's checked here first.
    private static T NotNull<T>(T member, string parameterName) where T : class
    {
        ArgumentNullException.ThrowIfNull(member, parameterName);
        return member;
    }
}
