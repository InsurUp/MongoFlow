using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// A write the concurrency token guarded in one save: the condition it added, and the document's token as read, which
/// it incremented on the document for a replace or an update.
/// </summary>
internal readonly record struct TokenGuard<TDocument, TVersioned, TToken>(
    VaultOperation<TDocument> Operation,
    BsonDocument Condition,
    TVersioned Document,
    TToken Read,
    bool Incremented);
