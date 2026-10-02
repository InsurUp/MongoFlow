using MongoDB.Bson;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Customers, and their consents, which a composite key looks up.</summary>
public sealed class CustomerService(CustomerVault vault,
    ICurrentUser user,
    TimeProvider clock)
{
    public async Task<Customer> RegisterAsync(string fullName, string email, CancellationToken cancellationToken)
    {
        var customer = new Customer { FullName = fullName, Email = email, OwnerUserId = user.UserId! };
        vault.Customers.Add(customer);

        // The driver gives the customer its ObjectId as it's inserted.
        await vault.SaveAsync(cancellationToken);

        return customer;
    }

    public async Task GiveConsentAsync(ObjectId customerId, ConsentPurpose purpose, CancellationToken cancellationToken)
    {
        vault.Consents.Add(new Consent { CustomerId = customerId, Purpose = purpose, GivenAt = clock.GetUtcNow() });
        await vault.SaveAsync(cancellationToken);
    }

    public async Task<bool> HasConsentAsync(ObjectId customerId, ConsentPurpose purpose, CancellationToken cancellationToken) =>
        await vault.Consents.GetByKeyAsync(new ConsentKey(customerId, purpose), cancellationToken) is not null;

    public Task<Customer?> FindAsync(ObjectId customerId, CancellationToken cancellationToken) =>
        vault.Customers.GetByKeyAsync(customerId, cancellationToken);
}
