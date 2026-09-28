using Microsoft.Extensions.Options;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Configuration;

public sealed class CustomerOptions
{
    /// <summary>Erase customers for real on delete, as data-protection law requires in some markets.</summary>
    public bool HardDeleteCustomers { get; set; }
}

/// <summary>
/// Setup for one vault kept outside the vault class, because it depends on options. It's created from the root
/// provider once, at startup.
/// </summary>
public sealed class CustomerVaultConfiguration(IOptions<CustomerOptions> options) : IVaultConfiguration<CustomerVault>
{
    public void Configure(IVaultBuilder<CustomerVault> vault) => vault
        .Collection(x => x.Customers, customers =>
        {
            // Duplicates merged into another record stay in the database for history but are never shown.
            customers.QueryFilter(customer => customer.Status != Domain.CustomerStatus.Merged);

            if (options.Value.HardDeleteCustomers)
            {
                customers.Without(SoftDeleteFeature.Key);
            }
        })
        .Collection(x => x.Agencies, agencies => agencies.Key(agency => agency.AgencyId));
}
