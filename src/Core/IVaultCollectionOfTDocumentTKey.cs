using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection whose documents are looked up by a <typeparamref name="TKey"/>.</summary>
public interface IVaultCollection<TDocument, TKey> : IVaultCollection<TDocument>
{
    /// <inheritdoc cref="IVaultCollection{TDocument}.Without"/>
    new IVaultCollection<TDocument, TKey> Without(FeatureKey feature);

    /// <summary>
    /// A view of this collection whose reads track the documents they return, whether or not the vault tracks changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vault's save compares each tracked document with what it was when read, and writes what changed as one update by
    /// the key it was read with: changed fields are set, removed ones unset, embedded documents compared field by field
    /// and arrays set whole. A key can't change. Unchanged documents aren't written. Once a save writes a document's
    /// changes, later saves compare against what it wrote; if the save fails or its transaction rolls back, the changes are
    /// pending again.
    /// </para>
    /// <para>
    /// <see cref="GetByKeyAsync"/>, <c>FindAsync</c> and <see cref="IVaultCollection{TDocument}.QueryAsync"/> track what
    /// they return, unless it's a projection. Aggregations aren't tracked. Queuing <see cref="Replace"/> or
    /// <see cref="Delete"/> of a tracked document takes the place of its changes; a delete stops tracking it.
    /// </para>
    /// <para>
    /// Each tracked document is kept, with a copy of its BSON, until the vault's scope ends: read what won't change through
    /// <see cref="WithNoTracking"/>.
    /// </para>
    /// </remarks>
    IVaultCollection<TDocument, TKey> WithTracking();

    /// <summary>A view of this collection whose reads don't track the documents they return, even if the vault tracks changes.</summary>
    IVaultCollection<TDocument, TKey> WithNoTracking();

    /// <summary>The document with <paramref name="key"/>, if the query filters let the caller see it.</summary>
    Task<TDocument?> GetByKeyAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>Queues a replace of the document with the same key.</summary>
    void Replace(TDocument document);

    /// <summary>Queues a delete of the document with the same key.</summary>
    void Delete(TDocument document);

    /// <summary>Queues a delete by key without reading the document.</summary>
    void DeleteByKey(TKey key);

    /// <summary>Queues an update by key without reading the document.</summary>
    /// <remarks>A concurrency token is incremented, but not checked: there's no read value to check it against.</remarks>
    void UpdateByKey(TKey key, UpdateDefinition<TDocument> update);

    /// <summary>
    /// Queues an update of the document with the same key. A concurrency token is checked against
    /// <paramref name="document"/>'s and incremented on it too; the update itself isn't applied to
    /// <paramref name="document"/>.
    /// </summary>
    void Update(TDocument document, UpdateDefinition<TDocument> update);
}
