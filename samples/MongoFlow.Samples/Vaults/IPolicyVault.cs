using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IPolicyVault : IMongoVault
{
    IVaultCollection<Policy, string> Policies { get; }

    IVaultCollection<Claim, Guid> Claims { get; }
}
