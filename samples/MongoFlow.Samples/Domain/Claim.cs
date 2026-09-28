namespace MongoFlow.Samples.Domain;

public enum ClaimStatus
{
    Open,
    Approved,
    Rejected,
    Paid
}

[RequiresPermission("claims.read")]
[Module("claims")]
public sealed class Claim : ITenantOwned
{
    public Guid Id { get; set; }

    public required string PolicyNumber { get; init; }

    public decimal Amount { get; set; }

    public ClaimStatus Status { get; set; }

    public AgencyId? AgencyId { get; set; }
}
