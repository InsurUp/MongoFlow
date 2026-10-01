namespace MongoFlow.IntegrationTests;

/// <summary>Hides the orders of the customer named <see cref="Customer"/>; switched off with its key.</summary>
public sealed class HiddenFeature : IVaultFeature
{
    public const string Customer = "hidden";

    public static FeatureKey Key { get; } = new("hidden");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
        vault.QueryFilter<Order>(x => x.Customer != Customer);
}
