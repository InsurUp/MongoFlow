namespace MongoFlow;

internal sealed class VaultRegistration<TVault> where TVault : MongoVault
{
    public List<Action<IServiceProvider, IVaultBuilder<TVault>>> Configurations { get; } = [];
}
