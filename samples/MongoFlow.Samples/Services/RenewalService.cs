using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Many policies at once: read from parallel tasks, changed, and saved once.</summary>
public sealed class RenewalService(IPolicyVault vault)
{
    // With no transaction open, one vault instance serves reads from parallel tasks, and writes can be queued from them
    // too; each policy read here is tracked. A save can't run alongside another save of the same vault, so there's one,
    // at the end. Inside a transaction, reads and saves share its session, which runs one operation at a time.
    public async Task<long> RenewAsync(IEnumerable<string> policyNumbers, DateTime until, CancellationToken cancellationToken)
    {
        var policies = await Task.WhenAll(policyNumbers.Select(number => vault.Policies.GetByKeyAsync(number, cancellationToken)));

        foreach (var policy in policies.OfType<Policy>())
        {
            policy.EndsAt = until;
        }

        var result = await vault.SaveAsync(cancellationToken);

        return result.Modified;
    }
}
