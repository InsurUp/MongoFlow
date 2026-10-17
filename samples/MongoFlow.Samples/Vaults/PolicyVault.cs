using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Interceptors;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// Configures its own shape: keys, indexes, concurrency, change tracking, a collection name inherited from an older system,
/// a guard on claims, and its migrations. Anything environment-specific, like the database, stays at registration.
/// </summary>
[MongoVersion("2.0.0")] // the migration its data is at; a release lowering it reverts the ones above
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    /// <summary>Keyed by <see cref="Claim.Id"/>, the member the driver maps to <c>_id</c>, so no key is configured.</summary>
    public IVaultCollection<Claim, Guid> Claims { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        // The migrator creates the indexes after the migrations; CreatePolicyIndexes applies them earlier, for the ones after it.
        .Collection(x => x.Policies, policies => policies
            .Key(p => p.PolicyNumber)
            .Index(keys => keys.Ascending(p => p.PolicyNumber), index => index.Unique()) // lookups by key expect one document
            .Index(keys => keys.Ascending(p => p.AgencyId).Ascending(p => p.Status).Descending(p => p.StartsAt))
            .Index(keys => keys.Ascending(p => p.EndsAt), index => index.Partial(p => p.Status == PolicyStatus.Active)))
        .Collection(x => x.Claims, claims => claims
            .Name("insurance_claims") // the vault's own setting, so it beats the snake_case default
            .AddInterceptor(new ClaimDecisionGuard())
            .Index(keys => keys.Ascending(c => c.PolicyNumber)))
        .UseConcurrencyToken((Policy p) => p.Version)
        // Policies are changed where they're read: the save writes what changed. Lists read without tracking.
        .UseChangeTracking()
        // Claims record when they were deleted. Customers and policies use the platform's flag.
        .UseSoftDelete((IDeletedAt x) => x.DeletedAt)
        .Migrations(m => m.AddFromAssemblyOf<PolicyVault>());
}
