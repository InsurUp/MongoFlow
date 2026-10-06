using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection without a key: it can be queried and written to, but not looked up by key.</summary>
/// <remarks>
/// <para>
/// The server still gives every document an <c>_id</c>, so a keyless document type must ignore it on reads, for example
/// with <c>[BsonIgnoreExtraElements]</c>. That's checked at startup.
/// </para>
/// <para>
/// Reads apply the collection's query filters, and inside a transaction they use its session. Query filters can be
/// asynchronous, so every read starts with one await that resolves them; what it returns is the driver's own type with
/// the filters already applied.
/// </para>
/// <para>
/// Writes are queued as <see cref="VaultOperation"/>s and sent when the vault is saved. They can be queued from parallel
/// tasks, and reads can run in parallel while no transaction is open; see <see cref="IMongoVault"/>.
/// </para>
/// </remarks>
public interface IVaultCollection<TDocument>
{
    /// <summary>The driver's collection. Reads and writes through it bypass query filters, features and the vault's save.</summary>
    IMongoCollection<TDocument> MongoCollection { get; }

    /// <summary>A view of this collection with a feature switched off for the reads and writes made through it.</summary>
    IVaultCollection<TDocument> Without(FeatureKey feature);

    /// <summary>
    /// A LINQ query of the collection, starting from its query filters. Joined with another collection's query, it joins
    /// only what that query's filters show. The driver's operators in <c>MongoDB.Driver.Linq</c> work on it, except
    /// <c>GetClient()</c>, which needs the driver's own provider.
    /// </summary>
    ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the documents matching <paramref name="filter"/> and the query filters.</summary>
    ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds with a filter built with the driver, such as <c>Builders&lt;T&gt;.Filter.ElemMatch(...)</c>, joined with the
    /// query filters.
    /// </summary>
    ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default);

    /// <summary>An aggregation whose first stage matches the query filters.</summary>
    ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues an insert of <paramref name="document"/>.</summary>
    void Add(TDocument document);

    /// <summary>Queues one insert per document. Adding nothing queues nothing.</summary>
    void AddRange(IEnumerable<TDocument> documents);

    /// <summary>
    /// Queues <paramref name="update"/> of every document matching <paramref name="filter"/> and the query filters.
    /// </summary>
    void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update);

    /// <summary>
    /// Queues a delete of every document matching <paramref name="filter"/> and the query filters; with soft delete, an
    /// update that marks them.
    /// </summary>
    void DeleteMany(Expression<Func<TDocument, bool>> filter);
}
