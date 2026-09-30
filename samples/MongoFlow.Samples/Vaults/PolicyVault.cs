using MongoDB.Driver;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// Configures its own shape: keys, indexes, concurrency, a collection name inherited from an older system, and its
/// migrations. Anything environment-specific, like the database, stays at registration.
/// </summary>
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    /// <summary>Keyed by <see cref="Claim.Id"/>, the member the driver maps to <c>_id</c>, so no key is configured.</summary>
    public IVaultCollection<Claim, Guid> Claims { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, policies => policies
            .Key(p => p.PolicyNumber) // also declares a unique index on PolicyNumber
            .ConcurrencyToken(p => p.Version)
            .Index(i => i.Ascending(p => p.AgencyId).Ascending(p => p.Status).Descending(p => p.StartsAt))
            .Index(i => i.Ascending(p => p.EndsAt), o => o.PartialFilterExpression =
                Builders<Policy>.Filter.Eq(p => p.Status, PolicyStatus.Active)))
        .Collection(x => x.Claims, claims => claims
            .Name("insurance_claims") // runs after the defaults, so it beats snake_case
            .Index(i => i.Ascending(c => c.PolicyNumber))
            .Settings(s => s.ReadConcern = ReadConcern.Majority))
        // Claims record when they were deleted. Customers and policies use the platform's flag.
        .UseSoftDelete((IDeletedAt x) => x.DeletedAt)
        .Migrations(m => m.AddFromAssemblyOf<PolicyVault>());
}
