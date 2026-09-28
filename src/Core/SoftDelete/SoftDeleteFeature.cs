using System.Linq.Expressions;

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
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete>(isDeleted));
}

internal sealed class SoftDeleteFeature<TSoftDelete> : IVaultFeature
{
    private readonly Expression<Func<TSoftDelete, bool>> _notDeleted;

    public SoftDeleteFeature(Expression<Func<TSoftDelete, bool>> isDeleted)
    {
        ArgumentNullException.ThrowIfNull(isDeleted);

        _notDeleted = Expression.Lambda<Func<TSoftDelete, bool>>(Expression.Not(isDeleted.Body), isDeleted.Parameters);
    }

    public FeatureKey Key => SoftDeleteFeature.Key;

    // TODO: Turn deletes into updates that set the flag once the save pipeline lets a feature change operations.
    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
        vault.QueryFilter(_notDeleted);
}
