using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Samples.Domain;

/// <summary>
/// Append-only and never looked up by key, so its collection is keyless. The server still adds an <c>_id</c>, which the
/// class has to ignore on reads.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AuditLogEntry : ITenantOwned
{
    public DateTime At { get; init; }

    public required string Collection { get; init; }

    public required string Action { get; init; }

    public string? UserId { get; init; }

    public BsonDocument? Before { get; init; }

    public BsonDocument? After { get; init; }

    public AgencyId? AgencyId { get; set; }
}
