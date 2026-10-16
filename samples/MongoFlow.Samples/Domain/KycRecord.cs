using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Samples.Domain;

/// <summary>
/// What an agency verified about a customer's identity. The national id, birth date and notes are stored encrypted, with
/// Queryable Encryption: see <see cref="Vaults.KycVault"/>. It ignores extra elements, as encrypted documents need: the
/// server adds <c>__safeContent__</c> to each.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class KycRecord : ITenantOwned
{
    public ObjectId Id { get; set; }

    public required string FullName { get; set; }

    public required string NationalId { get; set; }

    public DateTime BirthDate { get; set; }

    public string? Notes { get; set; }

    public AgencyId? AgencyId { get; set; }
}
