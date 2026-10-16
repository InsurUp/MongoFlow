using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Identity checks, stored encrypted and queried by their encrypted members as if they weren't.</summary>
public sealed class KycService(KycVault vault,
    IMongoClient client)
{
    public async Task RecordAsync(KycRecord record, CancellationToken cancellationToken)
    {
        vault.KycRecords.Add(record);
        await vault.SaveAsync(cancellationToken);
    }

    // MongoFlow encrypts the national id the filter compares with: the server holds it encrypted.
    public Task<KycRecord?> FindByNationalIdAsync(string nationalId, CancellationToken cancellationToken) =>
        vault.KycRecords.FirstOrDefaultAsync(x => x.NationalId == nationalId, cancellationToken);

    // A range query on an encrypted date, as LINQ.
    public async Task<List<string>> BornBeforeAsync(DateTime date, CancellationToken cancellationToken)
    {
        var records = await vault.KycRecords.QueryAsync(cancellationToken);

        return await records.Where(x => x.BirthDate < date).OrderBy(x => x.FullName).Select(x => x.FullName).ToListAsync(cancellationToken);
    }

    public async Task AddNoteAsync(string nationalId, string note, CancellationToken cancellationToken)
    {
        vault.KycRecords.UpdateMany(x => x.NationalId == nationalId, Builders<KycRecord>.Update.Set(x => x.Notes, note));
        await vault.SaveAsync(cancellationToken);
    }

    /// <summary>
    /// A record as the server holds it, read through the client the vault was given, which doesn't decrypt; and how many
    /// records the copy kept from before encryption holds, in plain text.
    /// </summary>
    public async Task<(BsonDocument? Stored, long Unencrypted)> AsStoredAsync(CancellationToken cancellationToken)
    {
        var name = vault.KycRecords.MongoCollection.CollectionNamespace;
        var database = client.GetDatabase(name.DatabaseNamespace.DatabaseName);

        var stored = await database.GetCollection<BsonDocument>(name.CollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .FirstOrDefaultAsync(cancellationToken);
        var unencrypted = await database.GetCollection<BsonDocument>($"{name.CollectionName}.unencrypted")
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);

        return (stored, unencrypted);
    }
}
