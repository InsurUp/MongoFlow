namespace MongoFlow.Samples.Interceptors;

/// <summary>A save tried to change a claim that had been approved, rejected or paid, or that isn't there.</summary>
public sealed class ClaimAlreadyDecidedException(Guid claimId)
    : InvalidOperationException($"Claim {claimId} was decided already, or isn't there, so it can't change.")
{
    public Guid ClaimId { get; } = claimId;
}
