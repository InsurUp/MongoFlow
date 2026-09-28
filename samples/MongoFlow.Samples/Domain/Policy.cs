using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

public enum PolicyStatus
{
    Draft,
    Active,
    Cancelled,
    Expired
}

public sealed record PolicyIssued(string PolicyNumber);

[RequiresPermission("policies.read")]
[Module("policies")]
public sealed class Policy : ITenantOwned, ISoftDeletable, IOwnedByUser, ITimestamped, IRaisesEvents
{
    private readonly List<object> _events = [];

    public ObjectId Id { get; set; }

    /// <summary>What people look policies up by, so it's the collection's key rather than <see cref="Id"/>.</summary>
    public required string PolicyNumber { get; init; }

    public ObjectId CustomerId { get; set; }

    public PolicyStatus Status { get; set; }

    public decimal Premium { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public bool HasOpenClaim { get; set; }

    /// <summary>The concurrency token: a replace fails if it changed since the policy was read.</summary>
    public int Version { get; set; }

    public required string OwnerUserId { get; init; }

    public AgencyId? AgencyId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public void Raise(object @event) => _events.Add(@event);

    public IReadOnlyList<object> TakeEvents()
    {
        var events = _events.ToList();
        _events.Clear();

        return events;
    }
}
