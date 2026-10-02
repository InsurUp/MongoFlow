using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>
/// A transaction spanning two vaults, rolled back when a rule fails halfway, claims decided only once, and claims joined
/// with their policies.
/// </summary>
public sealed class ClaimService(IPolicyVault policies,
    CustomerVault customers,
    IVaultTransactionManager transactions)
{
    /// <summary>How many open claims a customer may have; one more is refused.</summary>
    public const int MaxOpenClaims = 2;

    // Filing a claim records it, flags the policy and counts it on the customer. Both saves join the transaction, so all
    // three changes commit together or not at all. Inside a transaction, reads and saves share its session and run one
    // after another.
    public async Task<bool> FileAsync(Claim claim, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);

        var policy = await policies.Policies.GetByKeyAsync(claim.PolicyNumber, cancellationToken)
            ?? throw new KeyNotFoundException(claim.PolicyNumber);

        policies.Claims.Add(claim);
        policy.HasOpenClaim = true; // the policy is tracked, so the save writes the flag
        await policies.SaveAsync(cancellationToken);

        var customer = await customers.Customers.GetByKeyAsync(policy.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException(policy.CustomerId.ToString());

        // Checked after the first save to show a rollback undoing it; checking first would save the round trip.
        if (customer.OpenClaims >= MaxOpenClaims)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        customers.Customers.UpdateByKey(customer.Id, Builders<Customer>.Update.Inc(c => c.OpenClaims, 1));
        await customers.SaveAsync(cancellationToken);

        // Without this, disposing the transaction rolls it back.
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // By key, without a read: ClaimDecisionGuard makes the update apply only while the claim is open, and throws
    // ClaimAlreadyDecidedException when it isn't.
    public async Task DecideAsync(Guid claimId, ClaimStatus decision, CancellationToken cancellationToken)
    {
        policies.Claims.UpdateByKey(claimId, Builders<Claim>.Update.Set(c => c.Status, decision));
        await policies.SaveAsync(cancellationToken);
    }

    // Joined with another collection's query, a query joins only what that query's filters show: here the policies of
    // the user's agency, not deleted, and only for a user who may read them.
    public async Task<List<OpenClaim>> OpenWithPoliciesAsync(CancellationToken cancellationToken)
    {
        var open = from claim in await policies.Claims.QueryAsync(cancellationToken)
                   join policy in await policies.Policies.QueryAsync(cancellationToken)
                       on claim.PolicyNumber equals policy.PolicyNumber
                   where claim.Status == ClaimStatus.Open
                   orderby claim.Amount
                   select new OpenClaim(claim.Amount, policy.PolicyNumber, policy.Premium);

        return await open.ToListAsync(cancellationToken);
    }

    public async Task<List<Claim>> ForPolicyAsync(string policyNumber, CancellationToken cancellationToken)
    {
        var claims = await policies.Claims.QueryAsync(cancellationToken);

        return await claims.Where(c => c.PolicyNumber == policyNumber).ToListAsync(cancellationToken);
    }
}
