#if MISSING_API
// A transaction spanning two vaults.

using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Missing;

public sealed class ClaimService(IPolicyVault policies, IAuditVault audit, IVaultTransactions transactions)
{
    // Filing a claim bumps the policy's version and records an audit entry. Both vaults' saves commit or neither does.
    public async Task FileAsync(Claim claim, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);

        var policy = await policies.Policies.GetByKeyAsync(claim.PolicyNumber, cancellationToken)
            ?? throw new KeyNotFoundException(claim.PolicyNumber);

        policies.Claims.Add(claim);
        policy.Version++;
        policies.Policies.Replace(policy);
        await policies.SaveAsync(cancellationToken);

        audit.Entries.Add(new AuditLogEntry { At = DateTime.UtcNow, Collection = "claims", Action = "filed" });
        await audit.SaveAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
#endif
