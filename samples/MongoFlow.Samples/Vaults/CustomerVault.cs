using MongoDB.Bson;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>Configured by <see cref="Configuration.CustomerVaultConfiguration"/>, because its setup depends on options.</summary>
public sealed class CustomerVault : MongoVault
{
    public IVaultCollection<Customer, ObjectId> Customers { get; init; } = null!;

    public IVaultCollection<Agency, AgencyId> Agencies { get; init; } = null!;
}
