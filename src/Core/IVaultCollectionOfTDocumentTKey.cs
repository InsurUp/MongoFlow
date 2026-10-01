using MongoDB.Driver;

namespace MongoFlow;

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
    /// <remarks>A concurrency token is incremented, but not checked: there's no read value to check it against.</remarks>
    void UpdateByKey(TKey key, UpdateDefinition<TDocument> update);

    /// <summary>
    /// Queues an update of the document with the same key. A concurrency token is checked against
    /// <paramref name="document"/>'s and incremented on it too; the update itself isn't applied to
    /// <paramref name="document"/>.
    /// </summary>
    void Update(TDocument document, UpdateDefinition<TDocument> update);
}
