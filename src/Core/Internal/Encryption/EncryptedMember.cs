using MongoDB.Bson.Serialization;

namespace MongoFlow;

/// <summary>A member a collection declared encrypted, rendered with the collection's serializers.</summary>
/// <param name="Path">Its element path, such as <c>Address.Street</c>.</param>
/// <param name="Serializer">Its serializer, which tells its BSON type and serializes values compared with it.</param>
/// <param name="Options">What MongoFlow.Encryption declared it with, such as the queries it allows.</param>
internal sealed record EncryptedMember(string Path,
    IBsonSerializer Serializer,
    object Options);
