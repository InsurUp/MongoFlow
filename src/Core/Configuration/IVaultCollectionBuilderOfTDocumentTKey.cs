using System.Linq.Expressions;

namespace MongoFlow;

/// <summary>Configures a keyed collection of <typeparamref name="TDocument"/> looked up by <typeparamref name="TKey"/>.</summary>
public interface IVaultCollectionBuilder<TDocument, TKey> : IVaultCollectionBuilder<TDocument>
{
    /// <summary>
    /// What documents are looked up by: a member, such as <c>p =&gt; p.PolicyNumber</c>, or for a composite key a
    /// <typeparamref name="TKey"/> built from members, such as <c>t =&gt; new TokenKey(t.UserId, t.Provider)</c>, whose
    /// constructor arguments are matched to those members. Without it, the key is the member the driver maps to
    /// <c>_id</c>, whose type must be <typeparamref name="TKey"/>; that is checked at startup.
    /// </summary>
    /// <remarks>
    /// Lookups by key expect one document, so a key other than <c>_id</c> needs a unique index, which MongoFlow doesn't
    /// create.
    /// </remarks>
    /// <param name="key">The key member, or a new <typeparamref name="TKey"/> of members.</param>
    IVaultCollectionBuilder<TDocument, TKey> Key(Expression<Func<TDocument, TKey>> key);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> Name(string name);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> QueryFilter(Expression<Func<TDocument, bool>> filter);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> QueryFilter(Func<IServiceProvider, Expression<Func<TDocument, bool>>> filter);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> QueryFilter(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TDocument, bool>>>> filter);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> Without(FeatureKey feature);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor;

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> AddInterceptor(VaultInterceptor interceptor,
        Action<IInterceptorBuilder>? configure = null);
}
