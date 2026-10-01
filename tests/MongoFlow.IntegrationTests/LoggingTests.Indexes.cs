using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class LoggingTests
{
    [Test]
    public async Task IndexCheck_CollectionsMissingIndexes_WarnsAboutEach()
    {
        // Arrange — keyed with no index, with a unique index in another field order, with an index that isn't unique;
        // soft-deleted without and with an index on the flag; a tenant field in a compound index; a collection not created.
        var database = Mongo.NewDatabase();
        await database.CreateCollectionAsync("Policies");
        await CreateIndexAsync(database, "Tokens", new BsonDocument { { "provider", 1 }, { "UserId", 1 } }, unique: true);
        await CreateIndexAsync(database, "Receipts", new BsonDocument("Code", 1), unique: false);
        await database.CreateCollectionAsync("Articles");
        await CreateIndexAsync(database, "Pages", new BsonDocument { { "Slug", 1 }, { "IsDeleted", 1 } }, unique: false);
        await CreateIndexAsync(database, "Bills", new BsonDocument { { "TenantId", 1 }, { "_id", 1 } }, unique: false);
        await using var host = new VaultHost<IndexedVault>(Mongo.Client, database, vault => vault
            .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
            .UseMultiTenancy((Bill x) => x.TenantId, _ => "t-1"), Logging);

        // Act
        _ = host.Vault;
        await _sink.WaitForAsync(MongoFlowLogEvents.Indexes.IndexCheckCompleted);

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Indexes"));
    }

    [Test]
    public async Task IndexCheck_AWildcardIndex_CoversEveryField()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await CreateIndexAsync(database, "Articles", new BsonDocument("$**", 1), unique: false);
        await using var host = new VaultHost<IndexedVault>(Mongo.Client, database,
            vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted), Logging);

        // Act
        _ = host.Vault;
        await _sink.WaitForAsync(MongoFlowLogEvents.Indexes.IndexCheckCompleted);

        // Assert
        await Assert.That(_sink.Snapshot("MongoFlow.Indexes").Where(entry => entry.Message.Contains(".Articles"))).IsEmpty();
    }

    private static Task<string> CreateIndexAsync(IMongoDatabase database,
        string collection,
        BsonDocument keys,
        bool unique) =>
        database.GetCollection<BsonDocument>(collection).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions { Unique = unique }));
}
