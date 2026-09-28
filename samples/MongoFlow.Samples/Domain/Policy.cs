using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

public enum PolicyStatus
{
    Draft,
    Active,
    Cancelled,
    Expired
}

[RequiresPermission("policies.read")]
[Module("policies")]
public sealed class Policy : ITenantOwned, ISoftDeletable, IOwnedByUser
{
    public ObjectId Id { get; set; }

    /// <summary>What people look policies up by, so it's the collection's key rather than <see cref="Id"/>.</summary>
    public required string PolicyNumber { get; init; }

    public ObjectId CustomerId { get; set; }

    public PolicyStatus Status { get; set; }

    public decimal Premium { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public int Version { get; set; }

    public required string OwnerUserId { get; init; }

    public AgencyId? AgencyId { get; set; }

    public bool IsDeleted { get; set; }
}
