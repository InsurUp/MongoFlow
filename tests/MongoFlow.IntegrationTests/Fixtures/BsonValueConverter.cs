using Argon;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace MongoFlow.IntegrationTests;

/// <summary>Writes BSON in snapshots as the relaxed extended JSON a reader knows, instead of the .NET members of its classes.</summary>
public sealed class BsonValueConverter : WriteOnlyJsonConverter<BsonValue>
{
    private static readonly JsonWriterSettings Relaxed = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };

    public override void Write(VerifyJsonWriter writer, BsonValue value) => writer.Serialize(JToken.Parse(value.ToJson(Relaxed)));
}
