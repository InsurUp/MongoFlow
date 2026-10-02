namespace MongoFlow.IntegrationTests;

/// <summary>Collections keyed by something other than <c>_id</c>: a member, and a composite of two.</summary>
public sealed class InsuranceVault : MongoVault, IConfigurableVault<InsuranceVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    public IVaultCollection<LoginToken, TokenKey> Tokens { get; init; } = null!;

    public static void Configure(IVaultBuilder<InsuranceVault> vault) => vault
        .Collection(x => x.Policies, policies => policies.Key(p => p.Number))
        .Collection(x => x.Tokens, tokens => tokens.Key(t => new TokenKey(t.UserId, t.Provider)));
}
