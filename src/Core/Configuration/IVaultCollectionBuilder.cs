using System.Linq.Expressions;
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
