using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection without a key: it can be queried and written to, but not looked up by key.</summary>
/// <remarks>
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

/// <summary>A vault collection whose documents are looked up by a <typeparamref name="TKey"/>.</summary>
public interface IVaultCollection<TDocument, TKey> : IVaultCollection<TDocument>
{
    /// <inheritdoc cref="IVaultCollection{TDocument}.Without"/>
    new IVaultCollection<TDocument, TKey> Without(FeatureKey feature);

    /// <summary>The document with <paramref name="key"/>, if the query filters let the caller see it.</summary>
    Task<TDocument?> GetByKeyAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>Queues a replace of the document with the same key.</summary>
    void Replace(TDocument document);

    /// <summary>Queues a delete of the document with the same key.</summary>
    void Delete(TDocument document);

    /// <summary>Queues a delete by key without reading the document.</summary>
    void DeleteByKey(TKey key);

    /// <summary>Queues an update by key without reading the document.</summary>
    void UpdateByKey(TKey key, UpdateDefinition<TDocument> update);
}
