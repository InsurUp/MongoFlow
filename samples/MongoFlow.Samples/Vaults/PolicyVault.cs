using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Interceptors;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// Configures its own shape: keys, concurrency, change tracking, a collection name inherited from an older system, a
/// guard on claims, and its migrations, which also create its indexes. Anything environment-specific, like the database,
/// stays at registration.
/// </summary>
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    /// <summary>Keyed by <see cref="Claim.Id"/>, the member the driver maps to <c>_id</c>, so no key is configured.</summary>
    public IVaultCollection<Claim, Guid> Claims { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, policies => policies
            .Key(p => p.PolicyNumber)) // its unique index comes from the CreatePolicyIndexes migration
        .Collection(x => x.Claims, claims => claims
            .Name("insurance_claims") // the vault's own setting, so it beats the snake_case default
            .AddInterceptor(new ClaimDecisionGuard()))
        .UseConcurrencyToken((Policy p) => p.Version)
        // Policies are changed where they're read: the save writes what changed. Lists read without tracking.
        .UseChangeTracking()
        // Claims record when they were deleted. Customers and policies use the platform's flag.
        .UseSoftDelete((IDeletedAt x) => x.DeletedAt)
        .Migrations(m => m.AddFromAssemblyOf<PolicyVault>());
}
