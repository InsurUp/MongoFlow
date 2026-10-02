using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

[RequiresPermission("customers.read")]
public sealed class Customer : ITenantOwned, ISoftDeletable, IOwnedByUser, ITimestamped
{
    public ObjectId Id { get; set; }

    public required string FullName { get; set; }

    public required string Email { get; set; }

    /// <summary>A duplicate folded into <see cref="MergedInto"/>; kept for history but never shown.</summary>
    public CustomerStatus Status { get; set; }

    public ObjectId? MergedInto { get; set; }

    public int OpenClaims { get; set; }

    public required string OwnerUserId { get; init; }

    public AgencyId? AgencyId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
