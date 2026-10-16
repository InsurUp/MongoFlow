using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// How a collection's encrypted fields are written and queried, implemented by MongoFlow.Encryption. Saves and reads hand
/// it what they send, as BSON, after the interceptors ran.
/// </summary>
/// <remarks>
/// Each method copies what it's handed before changing it, and returns it as it is when there's nothing to encrypt: a
/// save shares its rendered query filters between operations, and a filter given as BSON belongs to its caller.
/// </remarks>
internal interface ICollectionEncryption
{
    /// <summary>The element paths of the encrypted fields, such as <c>Ssn</c> or <c>Address.Street</c>.</summary>
    IReadOnlyList<string> Paths { get; }

    /// <summary>
    /// Whether the server refuses writes that change several documents at once, so a set-based update or delete is
    /// written one document at a time.
    /// </summary>
    bool SingleDocumentWrites { get; }

    /// <summary>Loads what encrypting takes, such as the fields' key ids, once. A failure is tried again next time.</summary>
    ValueTask EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>A serialized document, with its encrypted fields' values encrypted, as an insert or a replace sends it.</summary>
    ValueTask<BsonDocument> EncryptDocumentAsync(BsonDocument document,
        CancellationToken cancellationToken);

    /// <summary>A rendered filter, with the values it compares encrypted fields with encrypted.</summary>
    /// <exception cref="NotSupportedException">It compares an encrypted field in a way the server can't.</exception>
    ValueTask<BsonDocument> EncryptFilterAsync(BsonDocument filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="EncryptFilterAsync"/>, for LINQ's synchronous execution. <see cref="EnsureReadyAsync"/> has run.
    /// </summary>
    BsonDocument EncryptFilter(BsonDocument filter);

    /// <summary>A rendered update, a document of operators or a pipeline, with the values it sets encrypted fields to encrypted.</summary>
    /// <exception cref="NotSupportedException">It changes an encrypted field in a way the server can't.</exception>
    ValueTask<BsonValue> EncryptUpdateAsync(BsonValue update,
        CancellationToken cancellationToken);
}
