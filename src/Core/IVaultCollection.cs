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
/// Writes are queued as <see cref="VaultOperation"/>s and sent when the vault is saved.
/// </para>
/// </remarks>
public interface IVaultCollection<TDocument>
{
    /// <summary>The driver's collection. Reads and writes through it bypass query filters, features and the vault's save.</summary>
    IMongoCollection<TDocument> MongoCollection { get; }

    /// <summary>A view of this collection with a feature switched off for the reads and writes made through it.</summary>
    IVaultCollection<TDocument> Without(FeatureKey feature);

    ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default);

    ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    /// <summary>An aggregation whose first stage matches the query filters.</summary>
    ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default);

    void Add(TDocument document);

    /// <summary>Queues one insert per document. Adding nothing queues nothing.</summary>
    void AddRange(IEnumerable<TDocument> documents);

    void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update);

    void DeleteMany(Expression<Func<TDocument, bool>> filter);
}
