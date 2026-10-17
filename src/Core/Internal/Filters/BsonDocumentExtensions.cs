using MongoDB.Bson;

namespace MongoFlow;

internal static class BsonDocumentExtensions
{
    /// <summary>Adds <paramref name="name"/> when <paramref name="value"/> isn't null, for an option left out when unset.</summary>
    public static BsonDocument AddIfNotNull(this BsonDocument document,
        string name,
        string? value) =>
        value is null ? document : document.Add(name, value);

    /// <inheritdoc cref="AddIfNotNull(BsonDocument, string, string?)"/>
    public static BsonDocument AddIfNotNull(this BsonDocument document,
        string name,
        BsonValue? value) =>
        value is null ? document : document.Add(name, value);
}
