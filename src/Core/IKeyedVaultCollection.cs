using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// A keyed vault collection without its type arguments: documents and keys are <see cref="object"/>s, updates BSON. Get
/// one from <see cref="IMongoVault.KeyedCollection(Type)"/>.
/// </summary>
/// <remarks>
/// It queues and reads what the typed collection does; see <see cref="IVaultCollection"/>. A key that isn't of
/// <see cref="IVaultCollectionInfo.KeyType"/> fails with <see cref="ArgumentException"/> when it's handed over: it isn't
/// converted.
/// </remarks>
public interface IKeyedVaultCollection : IVaultCollection
{
    /// <inheritdoc cref="IVaultCollection{TDocument}.Without"/>
    new IKeyedVaultCollection Without(FeatureKey feature);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.WithTracking"/>
    IKeyedVaultCollection WithTracking();

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.WithNoTracking"/>
    IKeyedVaultCollection WithNoTracking();

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.GetByKeyAsync"/>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't of the collection's key type.</exception>
    Task<object?> GetByKeyAsync(object key, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.Replace"/>
    /// <exception cref="ArgumentException"><paramref name="document"/> isn't a document of the collection.</exception>
    void Replace(object document);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.Delete"/>
    /// <exception cref="ArgumentException"><paramref name="document"/> isn't a document of the collection.</exception>
    void Delete(object document);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.DeleteByKey"/>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't of the collection's key type.</exception>
    void DeleteByKey(object key);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.UpdateByKey"/>
    /// <param name="key">The key of the document to update.</param>
    /// <param name="update">
    /// A document of update operators, or an array of pipeline stages; see <see cref="IVaultCollection.UpdateMany"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> isn't of the collection's key type, or <paramref name="update"/> isn't an update.
    /// </exception>
    void UpdateByKey(object key, BsonValue update);

    /// <inheritdoc cref="IVaultCollection{TDocument, TKey}.Update"/>
    /// <param name="document">The document to update, whose key and concurrency token the update is matched by.</param>
    /// <param name="update">
    /// A document of update operators, or an array of pipeline stages; see <see cref="IVaultCollection.UpdateMany"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/> isn't a document of the collection, or <paramref name="update"/> isn't an update.
    /// </exception>
    void Update(object document, BsonValue update);
}
