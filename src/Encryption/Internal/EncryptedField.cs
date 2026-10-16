using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>What a member was declared encrypted with: the queries it allows, and its BSON type if given.</summary>
internal sealed record EncryptedField(EncryptedQuery? Query,
    BsonType? BsonType);
