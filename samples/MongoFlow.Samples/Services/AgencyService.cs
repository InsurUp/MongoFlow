using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>The tenants themselves, looked up by a strongly typed key.</summary>
public sealed class AgencyService(CustomerVault vault)
{
    public async Task RegisterAsync(Agency agency, CancellationToken cancellationToken)
    {
        vault.Agencies.Add(agency);
        await vault.SaveAsync(cancellationToken);
    }

    public Task<Agency?> FindAsync(AgencyId agencyId, CancellationToken cancellationToken) =>
        vault.Agencies.GetByKeyAsync(agencyId, cancellationToken);
}
