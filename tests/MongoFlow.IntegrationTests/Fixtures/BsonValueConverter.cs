using Argon;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Writes BSON in snapshots as the relaxed extended JSON a reader knows, instead of the .NET members of its classes.
/// Encrypted values and the tags the server keeps beside them in <c>__safeContent__</c> differ each time they're written,
/// so they show as what they are: <c>(encrypted)</c> and <c>(tags)</c>.
/// </summary>
public sealed class BsonValueConverter : WriteOnlyJsonConverter<BsonValue>
{
    private const BsonBinarySubType Encrypted = (BsonBinarySubType)6;

    private static readonly JsonWriterSettings Relaxed = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };

    public override void Write(VerifyJsonWriter writer, BsonValue value) =>
        writer.Serialize(JToken.Parse(Stable(value).ToJson(Relaxed)));

    private static BsonValue Stable(BsonValue value) =>
        value switch
        {
            BsonBinaryData { SubType: Encrypted } => "(encrypted)",
            BsonDocument document => new BsonDocument(document.Select(element =>
                new BsonElement(element.Name, element.Name == "__safeContent__" ? "(tags)" : Stable(element.Value)))),
            BsonArray array => new BsonArray(array.Select(Stable)),
            _ => value
        };
}
