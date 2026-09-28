using System.Linq.Expressions;

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
}

/// <summary>Configures a keyed collection of <typeparamref name="TDocument"/> looked up by <typeparamref name="TKey"/>.</summary>
public interface IVaultCollectionBuilder<TDocument, TKey> : IVaultCollectionBuilder<TDocument>
{
    /// <summary>
    /// The member documents are looked up by, such as <c>p =&gt; p.PolicyNumber</c>. Without it, the key is the member
    /// the driver maps to <c>_id</c>, whose type must be <typeparamref name="TKey"/>; that is checked at startup.
    /// </summary>
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
}
