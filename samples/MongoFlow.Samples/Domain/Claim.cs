namespace MongoFlow.Samples.Domain;

[RequiresPermission("claims.read")]
[Module("claims")]
public sealed class Claim : ITenantOwned, IDeletedAt
{
    public Guid Id { get; set; }

    public required string PolicyNumber { get; init; }

    public decimal Amount { get; set; }

    public ClaimStatus Status { get; set; }

    public AgencyId? AgencyId { get; set; }

    public DateTime? DeletedAt { get; set; }
}
