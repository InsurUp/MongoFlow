using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Declares how a collection's index is built: its name and options. <c>IVaultMigrator.MigrateAllAsync</c> creates it after
/// the vault's migrations, and keeps it as declared.
/// </summary>
/// <remarks>
/// An index that differs from its declaration has its expiry, and whether it's hidden, changed in place. MongoDB can't
/// change anything else in place, so the migrator fails, naming the index, for a migration to recreate it with
/// <c>context.Collection(...).RecreateIndexAsync</c>.
/// </remarks>
public interface IIndexBuilder<TDocument>
{
    /// <summary>
    /// The index's name, which a migration recreates it by. Without it, the index is named as the server names one, such
    /// as <c>Customer_1_PlacedAt_-1</c>.
    /// </summary>
    IIndexBuilder<TDocument> Name(string name);

    /// <summary>No two documents can have the same values for its keys, such as a key other than <c>_id</c>.</summary>
    IIndexBuilder<TDocument> Unique(bool unique = true);

    /// <summary>Documents without its keys are left out of it.</summary>
    IIndexBuilder<TDocument> Sparse(bool sparse = true);

    /// <summary>
    /// Indexes only the documents <paramref name="filter"/> matches, such as <c>x =&gt; x.Status == Status.Active</c>; a
    /// unique index is unique among them alone.
    /// </summary>
    /// <remarks>
    /// The server takes equalities, comparisons, <c>$exists: true</c>, <c>$type</c> and <c>$and</c> in a partial filter.
    /// <c>x =&gt; x.Field != null</c> renders <c>$ne</c>, which it refuses: use <c>Builders&lt;T&gt;.Filter.Exists</c>.
    /// </remarks>
    IIndexBuilder<TDocument> Partial(Expression<Func<TDocument, bool>> filter);

    /// <inheritdoc cref="Partial(Expression{Func{TDocument, bool}})"/>
    IIndexBuilder<TDocument> Partial(FilterDefinition<TDocument> filter);

    /// <summary>
    /// Deletes each document once the date in the index's one field is older than <paramref name="expireAfter"/>: a TTL
    /// index. Changed in place.
    /// </summary>
    IIndexBuilder<TDocument> ExpireAfter(TimeSpan expireAfter);

    /// <summary>Hides it from queries while it's kept up to date, to see what dropping it would do. Changed in place.</summary>
    IIndexBuilder<TDocument> Hidden(bool hidden = true);

    /// <summary>
    /// How it compares strings, rather than as the collection does; a query uses it only when it compares them the same
    /// way.
    /// </summary>
    IIndexBuilder<TDocument> Collation(Collation collation);
}
