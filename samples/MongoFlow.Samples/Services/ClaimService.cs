using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>A transaction spanning two vaults.</summary>
public sealed class ClaimService(IPolicyVault policies, CustomerVault customers, IVaultTransactions transactions)
{
    // Filing a claim records it, bumps the policy's version and counts it on the customer. Both saves join the
    // transaction, so all three changes commit together or not at all.
    public async Task FileAsync(Claim claim, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);

        var policy = await policies.Policies.GetByKeyAsync(claim.PolicyNumber, cancellationToken)
            ?? throw new KeyNotFoundException(claim.PolicyNumber);

        policies.Claims.Add(claim);
        policies.Policies.UpdateByKey(policy.PolicyNumber, Builders<Policy>.Update.Inc(p => p.Version, 1));
        await policies.SaveAsync(cancellationToken);

        customers.Customers.UpdateByKey(policy.CustomerId, Builders<Customer>.Update.Inc(c => c.OpenClaims, 1));
        await customers.SaveAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
