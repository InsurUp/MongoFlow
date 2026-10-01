using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests;

/// <summary>A keyless document: it ignores the <c>_id</c> the server adds.</summary>
[BsonIgnoreExtraElements]
public sealed class AuditEntry : ITenantOwned
{
    public string Message { get; set; } = "";

    public string? TenantId { get; set; }
}
