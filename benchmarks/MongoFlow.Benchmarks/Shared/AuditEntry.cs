using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Benchmarks;

[BsonIgnoreExtraElements]
public sealed class AuditEntry
{
    public string Message { get; set; } = "";
}
