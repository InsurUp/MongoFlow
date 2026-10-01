using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

/// <summary>A keyless document: it ignores the <c>_id</c> the server adds.</summary>
[BsonIgnoreExtraElements]
public sealed class AuditEntry
{
    public string Message { get; set; } = "";
}
