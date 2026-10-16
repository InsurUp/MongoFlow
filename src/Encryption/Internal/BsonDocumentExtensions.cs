using MongoDB.Bson;

namespace MongoFlow.Encryption;

internal static class BsonDocumentExtensions
{
    /// <summary>Adds <paramref name="name"/> when <paramref name="value"/> isn't null, as a master key leaves out what it doesn't set.</summary>
    public static BsonDocument AddIfNotNull(this BsonDocument document,
        string name,
        string? value) =>
        value is null ? document : document.Add(name, value);
}
