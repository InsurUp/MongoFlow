using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Walkthrough;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>
/// KYC records as an earlier version of the platform stored them, in plain text, before it encrypted them, with the index
/// its tenant filter reads by. The migrator finds their collection holding documents it doesn't encrypt, and rebuilds it as
/// an encrypted one, index included.
/// </summary>
public static class LegacyKycRecords
{
    public const string Collection = "kyc_records";

    /// <summary>Stores them in <paramref name="database"/>, unless an earlier run did, and encrypted them since.</summary>
    public static async Task SeedAsync(IMongoDatabase database,
        CancellationToken cancellationToken)
    {
        var names = await (await database.ListCollectionNamesAsync(cancellationToken: cancellationToken)).ToListAsync(cancellationToken);
        if (names.Contains(Collection) || names.Contains($"{Collection}.unencrypted"))
        {
            return;
        }

        var records = database.GetCollection<KycRecord>(Collection);
        await records.Indexes.CreateOneAsync(new CreateIndexModel<KycRecord>(Builders<KycRecord>.IndexKeys.Ascending(x => x.AgencyId)),
            cancellationToken: cancellationToken);

        await records.InsertManyAsync(
        [
            new KycRecord
            {
                Id = ObjectId.GenerateNewId(), FullName = "Ada Lovelace", NationalId = "10000000146",
                BirthDate = new DateTime(1815, 12, 10, 0, 0, 0, DateTimeKind.Utc), Notes = "Passport checked", AgencyId = SampleUsers.Acme
            },
            new KycRecord
            {
                Id = ObjectId.GenerateNewId(), FullName = "Grace Hopper", NationalId = "10000000214",
                BirthDate = new DateTime(1906, 12, 9, 0, 0, 0, DateTimeKind.Utc), AgencyId = SampleUsers.Acme
            }
        ], cancellationToken: cancellationToken);
    }
}
