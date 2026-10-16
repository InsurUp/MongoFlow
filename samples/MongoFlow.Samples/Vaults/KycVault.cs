using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Encryption;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// Know-your-customer records, with their sensitive members encrypted: declared here, with the queries each allows. Which
/// KMS protects the keys is environment-specific, so it's set at registration, with <c>UseQueryableEncryption</c>.
/// </summary>
/// <remarks>
/// It skips the platform's defaults: the audit interceptor saves the audit vault in each change's transaction, and a vault
/// that encrypts uses a client of its own, which the audit vault isn't on. It limits reads to the agent's agency itself.
/// </remarks>
public sealed class KycVault : MongoVault, IConfigurableVault<KycVault>
{
    public IVaultCollection<KycRecord, ObjectId> KycRecords { get; init; } = null!;

    public static void Configure(IVaultBuilder<KycVault> vault) => vault
        .Collection(x => x.KycRecords, records => records
            .Encrypt(x => x.NationalId, EncryptedQuery.Equality)
            .Encrypt(x => x.BirthDate, EncryptedQuery.Range(new DateTime(1800, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
            .Encrypt(x => x.Notes))
        .UseMultiTenancy(
            (ITenantOwned x) => x.AgencyId,
            services => services.GetRequiredService<ICurrentUser>().AgencyId);
}
