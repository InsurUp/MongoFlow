using System.Linq.Expressions;
using System.Numerics;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Configures the collection of <typeparamref name="TDocument"/>.</summary>
/// <remarks>
/// <para>
/// Query filters apply to every read on the collection, key lookups included. A filter added from
/// <see cref="IVaultFeature.Configure{TVault}"/> belongs to that feature and is switched off with it; one added anywhere else
/// is always on.
/// </para>
/// <para>
/// A per-query filter returns <c>_ =&gt; true</c> to leave the query unfiltered or <c>_ =&gt; false</c> to match
/// nothing; both are recognized and folded away. Asynchronous filters are resolved when the query executes.
/// </para>
/// </remarks>
public interface IVaultCollectionBuilder<TDocument> : IVaultCollectionInfo
{
    /// <summary>The collection's name in the vault's database. Defaults to the property name.</summary>
    IVaultCollectionBuilder<TDocument> Name(string name);

    IVaultCollectionBuilder<TDocument> QueryFilter(Expression<Func<TDocument, bool>> filter);

    /// <summary>Adds a query filter decided per query from the request's services.</summary>
    IVaultCollectionBuilder<TDocument> QueryFilter(Func<IServiceProvider, Expression<Func<TDocument, bool>>> filter);

    /// <inheritdoc cref="QueryFilter(Func{IServiceProvider, Expression{Func{TDocument, bool}}})"/>
    IVaultCollectionBuilder<TDocument> QueryFilter(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TDocument, bool>>>> filter);

    /// <summary>Opts this collection out of a feature added to the vault.</summary>
    IVaultCollectionBuilder<TDocument> Without(FeatureKey feature);

    /// <summary>Declares an index, created by <see cref="IVaultMigrator.MigrateAllAsync"/>.</summary>
    /// <remarks>An existing index with the same keys but different options makes the migration fail.</remarks>
    IVaultCollectionBuilder<TDocument> Index(
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<CreateIndexOptions<TDocument>>? options = null);

    /// <summary>Driver settings for the collection, such as read preference and read concern.</summary>
    /// <remarks>
    /// A write concern set here doesn't affect saves: every save runs in a transaction, whose write concern applies
    /// instead.
    /// </remarks>
    IVaultCollectionBuilder<TDocument> Settings(Action<MongoCollectionSettings> configure);

    /// <summary>
    /// Options used when <see cref="IVaultMigrator.MigrateAllAsync"/> creates the collection, such as time-series,
    /// capped or a validator. An existing collection is left as it is.
    /// </summary>
    IVaultCollectionBuilder<TDocument> CreateWith(Action<CreateCollectionOptions<TDocument>> configure);

    /// <summary>
    /// Adds an interceptor that sees only this collection's operations. It's created from the request's services once
    /// per vault instance.
    /// </summary>
    IVaultCollectionBuilder<TDocument> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor;

    /// <summary>
    /// Adds an interceptor instance, shared by every request, that sees only this collection's operations. Being
    /// registered per collection, it can be written against <typeparamref name="TDocument"/>.
    /// </summary>
    IVaultCollectionBuilder<TDocument> AddInterceptor(VaultInterceptor interceptor,
        Action<IInterceptorBuilder>? configure = null);
}

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
