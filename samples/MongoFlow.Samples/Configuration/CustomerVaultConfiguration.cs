using Microsoft.Extensions.Options;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Configuration;

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
            customers.QueryFilter(customer => customer.Status != CustomerStatus.Merged);

            if (options.Value.HardDeleteCustomers)
            {
                customers.Without(SoftDeleteFeature.Key);
            }

            // Searched by name with MongoDB Search, in an index the migrator creates and waits for. The agency and the
            // soft-delete flag are mapped too, by MongoFlow, and the status here, so searches filter on them in the index.
            customers.SearchIndex("default", index => index
                .Map(customer => customer.FullName, SearchField.String(), SearchField.Autocomplete())
                .Map(customer => customer.Status, SearchField.Number()));
        })
        .Collection(x => x.Agencies, agencies => agencies.Key(agency => agency.AgencyId))
        // A composite key: the constructor's arguments are matched to the members they're built from.
        .Collection(x => x.Consents, consents => consents.Key(consent => new ConsentKey(consent.CustomerId, consent.Purpose)));
}
