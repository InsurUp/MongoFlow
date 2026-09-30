using System.Linq.Expressions;
using System.Numerics;
using MongoDB.Driver;

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
    /// <param name="key">The key member, or a new <typeparamref name="TKey"/> of members.</param>
    /// <param name="unique">
    /// Declares a unique index on the key, since lookups by key expect one document. Turn it off when uniqueness is per
    /// tenant, and declare a compound unique index instead.
    /// </param>
    IVaultCollectionBuilder<TDocument, TKey> Key(Expression<Func<TDocument, TKey>> key, bool unique = true);

    /// <summary>
    /// Optimistic concurrency. Replacing or deleting a document fails with <see cref="ConcurrencyException"/> if its
    /// token changed since it was read. Replaces and updates increment the token.
    /// </summary>
    IVaultCollectionBuilder<TDocument, TKey> ConcurrencyToken<TToken>(Expression<Func<TDocument, TToken>> token)
        where TToken : INumber<TToken>;

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
    new IVaultCollectionBuilder<TDocument, TKey> Index(
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<CreateIndexOptions<TDocument>>? options = null);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> Settings(Action<MongoCollectionSettings> configure);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> CreateWith(Action<CreateCollectionOptions<TDocument>> configure);

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor;

    /// <inheritdoc/>
    new IVaultCollectionBuilder<TDocument, TKey> AddInterceptor(VaultInterceptor interceptor,
        Action<IInterceptorBuilder>? configure = null);
}
