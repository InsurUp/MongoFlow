using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Syncing indexes on a sharded cluster, through mongos, keeps the index a sharded collection's shard key is on: one no
/// vault declares fails the migrator, naming it, before any index is changed, as the server would drop it on a hashed shard
/// key; once declared, what no vault declares is dropped as on a replica set.
/// </summary>
public partial class ShardedClusterTests
{
    [ClassDataSource<ShardedClusterFixture>(Shared = SharedType.PerTestSession)]
    public required ShardedClusterFixture Cluster { get; init; }

    [Test]
    public async Task MigrateAllAsync_SyncingAnIndexOnTheShardKeyNoVaultDeclares_ThrowsChangingNothing()
    {
        // Arrange — sharded on the customer, hashed, which the server indexes; an index no vault declares beside it.
        await using var host = await ShardedAsync(orders => orders.Index(keys => keys.Ascending(x => x.Number)));

        // Act
        var exception = await Assert.That(() => SyncAsync(host)).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Message = Scrubbed(exception!.Message), Indexes = await NamesAsync(host.Database) });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingAShardedCollectionThatDeclaresItsShardKeysIndex_DropsWhatNoVaultDeclares()
    {
        // Arrange
        await using var host = await ShardedAsync(orders => orders.Index(keys => keys.Hashed(x => x.Customer)));

        // Act
        await SyncAsync(host);

        // Assert
        await Assert.That(await NamesAsync(host.Database)).IsEquivalentTo(["_id_", "Customer_hashed"]);
    }

    // The orders' vault on a new database, the orders sharded on the customer, hashed, and indexed by note too.
    private async Task<VaultHost<OrdersVault>> ShardedAsync(Action<IVaultCollectionBuilder<Order, int>> orders)
    {
        var database = Cluster.NewDatabase();
        await Cluster.Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "shardCollection", $"{database.DatabaseNamespace.DatabaseName}.Orders" },
            { "key", new BsonDocument("Customer", "hashed") }
        });
        await database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));

        return new VaultHost<OrdersVault>(Cluster.Client, database, vault => vault.Collection(x => x.Orders, orders));
    }

    private static Task SyncAsync(VaultHost<OrdersVault> host) =>
        host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(new MigrateOptions { SyncIndexes = true });

    private static async Task<List<string>> NamesAsync(IMongoDatabase database) =>
    [
        .. (await (await database.GetCollection<BsonDocument>("Orders").Indexes.ListAsync()).ToListAsync())
            .Select(index => index["name"].AsString)
    ];

    private static string Scrubbed(string message) => Database().Replace(message, "{database}");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();
}
