using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IPolicyVault : IMongoVault
{
    IVaultCollection<Policy, string> Policies { get; }

    IVaultCollection<Claim, Guid> Claims { get; }
}

/// <summary>
/// Configures its own shape: what policies are keyed by, and a collection name inherited from an older system. Anything
/// environment-specific, like the database, stays at registration.
/// </summary>
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    /// <summary>Keyed by <see cref="Claim.Id"/>, the member the driver maps to <c>_id</c>, so no key is configured.</summary>
    public IVaultCollection<Claim, Guid> Claims { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, c => c.Key(p => p.PolicyNumber))
        .Collection(x => x.Claims, c => c.Name("insurance_claims")); // runs after the defaults, so it beats snake_case
}
