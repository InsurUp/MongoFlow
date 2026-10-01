using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// Configures its own shape: keys, concurrency and a collection name inherited from an older system. Anything
/// environment-specific, like the database, stays at registration.
/// </summary>
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    /// <summary>Keyed by <see cref="Claim.Id"/>, the member the driver maps to <c>_id</c>, so no key is configured.</summary>
    public IVaultCollection<Claim, Guid> Claims { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, policies => policies
            .Key(p => p.PolicyNumber)) // needs a unique index on PolicyNumber, created outside MongoFlow
        .Collection(x => x.Claims, claims => claims
            .Name("insurance_claims")) // the vault's own setting, so it beats the snake_case default
        .UseConcurrencyToken((Policy p) => p.Version)
        // Claims record when they were deleted. Customers and policies use the platform's flag.
        .UseSoftDelete((IDeletedAt x) => x.DeletedAt);
}
