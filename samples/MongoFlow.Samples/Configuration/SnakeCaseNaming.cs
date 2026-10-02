namespace MongoFlow.Samples.Configuration;

/// <summary>A second default, kept separate so a vault can skip the platform rules but keep the naming.</summary>
public sealed class SnakeCaseNaming<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault) => vault.ForEachCollection(new SnakeCaseCollectionNames());
}
