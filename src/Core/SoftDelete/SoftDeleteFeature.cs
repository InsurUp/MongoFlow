using System.Linq.Expressions;
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
        vault.AddFeature(new SoftDeleteFeature<TSoftDelete>(isDeleted));
}

internal sealed class SoftDeleteFeature<TSoftDelete> : IVaultFeature, IVaultCollectionConfiguration
{
    private readonly Expression<Func<TSoftDelete, bool>> _isDeleted;
    private readonly Expression<Func<TSoftDelete, bool>> _notDeleted;
    private readonly Action<TSoftDelete, bool> _setDeleted;

    public SoftDeleteFeature(Expression<Func<TSoftDelete, bool>> isDeleted)
    {
        ArgumentNullException.ThrowIfNull(isDeleted);

        _isDeleted = isDeleted;
        _notDeleted = Expression.Lambda<Func<TSoftDelete, bool>>(Expression.Not(isDeleted.Body), isDeleted.Parameters);
        _setDeleted = MemberExpressions.CreateSetter(isDeleted, nameof(isDeleted));
    }

    public FeatureKey Key => SoftDeleteFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter(_notDeleted)
        .ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TSoftDelete)))
        {
            collection.AddInterceptor(new SoftDeleteInterceptor<TDocument, TSoftDelete>(_isDeleted, _setDeleted));
        }
    }
}

/// <summary>Turns each delete into an update that sets the flag, and sets it on the deleted document too.</summary>
internal sealed class SoftDeleteInterceptor<TDocument, TSoftDelete>(
    Expression<Func<TSoftDelete, bool>> isDeleted,
    Action<TSoftDelete, bool> setDeleted) : VaultInterceptor
{
    private readonly UpdateDefinition<TDocument> _markDeleted =
        Builders<TDocument>.Update.Set(MemberExpressions.Rebind<TSoftDelete, TDocument, bool>(isDeleted), true);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var delete in context.Operations.OfType<DeleteOperation<TDocument>>().ToList())
        {
            if (delete.Document is TSoftDelete document)
            {
                setDeleted(document, true);
            }

            context.Replace(delete, delete.ToUpdate(_markDeleted));
        }

        return ValueTask.CompletedTask;
    }
}
