using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// A vault collection without its type arguments, for code that has the document type only as a <see cref="Type"/>,
/// such as an interceptor for every collection. Documents are <see cref="object"/>s, filters and updates BSON. Get one
/// from <see cref="IMongoVault.Collection(Type)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each write queues what the typed call queues: the same <see cref="InsertOperation{TDocument}"/>,
/// <see cref="UpdateOperation{TDocument}"/> or <see cref="DeleteOperation{TDocument}"/>, seen by the same interceptors
/// and features. What the compiler checks for a typed call is checked when the write is queued: a document that isn't of
/// <see cref="IVaultCollectionInfo.DocumentType"/>, or of a type derived from it, fails with
/// <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// Filters and updates are taken as rendered, as <see cref="VaultOperation.RenderFilter"/> and
/// <see cref="VaultOperation.RenderUpdate"/> give them, so an operation's can be queued again unchanged: field names are
/// element names, such as <c>_id</c>. A filter is joined with the collection's query filters, as a typed write's is.
/// They're kept as they are, not copied: don't change them once the write is queued.
/// </para>
/// <para>
/// Reads by filter are left to the typed collection, whose results are typed:
/// <see cref="IMongoVault.Collection{TDocument}"/>.
/// </para>
/// </remarks>
public interface IVaultCollection : IVaultCollectionInfo
{
    /// <inheritdoc cref="IVaultCollection{TDocument}.Without"/>
    IVaultCollection Without(FeatureKey feature);

    /// <inheritdoc cref="IVaultCollection{TDocument}.Add"/>
    /// <exception cref="ArgumentException"><paramref name="document"/> isn't a document of the collection.</exception>
    void Add(object document);

    /// <inheritdoc cref="IVaultCollection{TDocument}.AddRange"/>
    /// <exception cref="ArgumentException">A document isn't a document of the collection.</exception>
    void AddRange(IEnumerable<object> documents);

    /// <inheritdoc cref="IVaultCollection{TDocument}.UpdateMany"/>
    /// <param name="filter">The documents to update, such as <c>{ Customer: "ada" }</c>.</param>
    /// <param name="update">
    /// A document of update operators, such as <c>{ $set: { Total: 20 } }</c>, or an array of pipeline stages, such as
    /// <c>[{ $set: { Total: 20 } }]</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="update"/> is neither.</exception>
    void UpdateMany(BsonDocument filter, BsonValue update);

    /// <inheritdoc cref="IVaultCollection{TDocument}.DeleteMany"/>
    /// <param name="filter">The documents to delete, such as <c>{ Customer: "ada" }</c>.</param>
    void DeleteMany(BsonDocument filter);
}
