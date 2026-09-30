using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Everyday reads and writes against a keyed collection.</summary>
public sealed class PolicyService(IPolicyVault vault)
{
    // Lookup by the collection's key. The key is typed, so passing a Guid here is a compile error.
    public Task<Policy?> FindAsync(string policyNumber, CancellationToken cancellationToken) =>
        vault.Policies.GetByKeyAsync(policyNumber, cancellationToken);

    // The first await resolves the tenant, soft-delete, permission and module filters (the last two asynchronously);
    // everything after it is the driver's own LINQ.
    public async Task<List<Policy>> ActiveForCustomerAsync(ObjectId customerId, CancellationToken cancellationToken)
    {
        var policies = await vault.Policies.QueryAsync(cancellationToken);

        return await policies
            .Where(p => p.CustomerId == customerId && p.Status == PolicyStatus.Active)
            .OrderByDescending(p => p.StartsAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Policy> Items, long Total)> PageAsync(int page, int size, CancellationToken cancellationToken)
    {
        var policies = (await vault.Policies.QueryAsync(cancellationToken)).Where(p => p.Status != PolicyStatus.Draft);

        var total = await policies.LongCountAsync(cancellationToken);
        var items = await policies.OrderBy(p => p.PolicyNumber).Skip(page * size).Take(size).ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<List<PolicySummary>> SummariesAsync(CancellationToken cancellationToken)
    {
        var policies = await vault.Policies.QueryAsync(cancellationToken);

        return await policies
            .Select(p => new PolicySummary(p.PolicyNumber, p.Status, p.Premium))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Policy>> ExpiringAsync(DateTime before, CancellationToken cancellationToken)
    {
        var find = await vault.Policies.FindAsync(p => p.EndsAt < before, cancellationToken);

        return await find.SortBy(p => p.EndsAt).Limit(100).ToListAsync(cancellationToken);
    }

    public async Task<List<PremiumByStatus>> PremiumByStatusAsync(CancellationToken cancellationToken)
    {
        var aggregate = await vault.Policies.AggregateAsync(cancellationToken);

        return await aggregate
            .Group(p => p.Status, group => new PremiumByStatus(group.Key, group.Sum(p => p.Premium)))
            .ToListAsync(cancellationToken);
    }

    // Unit of work: nothing is written until SaveAsync, which sends everything queued as one bulk write.
    public async Task IssueAsync(Policy policy, CancellationToken cancellationToken)
    {
        policy.Raise(new PolicyIssued(policy.PolicyNumber)); // stored by the outbox in the same transaction
        vault.Policies.Add(policy);                           // multi-tenancy sets AgencyId, timestamps set CreatedAt

        await vault.SaveAsync(cancellationToken);
    }

    public async Task CancelAsync(string policyNumber, CancellationToken cancellationToken)
    {
        var policy = await FindAsync(policyNumber, cancellationToken) ?? throw new KeyNotFoundException(policyNumber);

        policy.Status = PolicyStatus.Cancelled;
        vault.Policies.Replace(policy); // fails with ConcurrencyException if someone saved it since the read

        await vault.SaveAsync(cancellationToken);
    }

    // No read first: soft delete turns this into an update that sets IsDeleted.
    public async Task DeleteAsync(string policyNumber, CancellationToken cancellationToken)
    {
        vault.Policies.DeleteByKey(policyNumber);
        await vault.SaveAsync(cancellationToken);
    }

    public async Task RenewAsync(string policyNumber, DateTime until, CancellationToken cancellationToken)
    {
        vault.Policies.UpdateByKey(policyNumber, Builders<Policy>.Update.Set(p => p.EndsAt, until)); // Version goes up by itself
        await vault.SaveAsync(cancellationToken);
    }

    // Set-based: one operation, whatever it matches.
    public async Task<long> ExpireEndedAsync(DateTime now, CancellationToken cancellationToken)
    {
        vault.Policies.UpdateMany(
            p => p.Status == PolicyStatus.Active && p.EndsAt < now,
            Builders<Policy>.Update.Set(p => p.Status, PolicyStatus.Expired));

        var result = await vault.SaveAsync(cancellationToken);

        return result.Modified;
    }

    // Features switched off for one read: a platform report across every agency, deleted policies included.
    public async Task<long> CountEverythingAsync(CancellationToken cancellationToken)
    {
        var policies = await vault.Policies
            .Without(MultiTenancyFeature.Key)
            .Without(SoftDeleteFeature.Key)
            .QueryAsync(cancellationToken);

        return await policies.LongCountAsync(cancellationToken);
    }

    // The driver, with no filters or features.
    public IMongoCollection<Policy> Raw => vault.Policies.MongoCollection;
}
