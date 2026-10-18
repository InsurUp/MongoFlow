using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class IndexTests
{
    [Test]
    public async Task MigrateAllAsync_SyncingAUniqueIndexDeclaredPlain_RecreatesItPlain()
    {
        // Arrange — unique by hand, as an index made plain since was left on a server.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number)));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Number), new CreateIndexOptions { Unique = true }));

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(new { Indexes = await IndexesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MigrateAllAsync_SyncingAPlainIndexDeclaredUnique_MakesItUniqueInPlace(bool hidden)
    {
        // Arrange — plain by hand, hidden or not, over orders of different numbers.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number), index => index.Unique()));
        await host.SeedAsync("Orders", new Order { Id = 1, Number = "A-1" }, new Order { Id = 2, Number = "A-2" });
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Number), new CreateIndexOptions { Hidden = hidden ? true : null }));

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(new { Indexes = await IndexesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingAnIndexDeclaredUniqueOverDuplicates_FailsLeavingEveryIndex()
    {
        // Arrange — two orders with one number, indexed plain, and an index no vault declares.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number), index => index.Unique()));
        await host.SeedAsync("Orders", new Order { Id = 1, Number = "A-1" }, new Order { Id = 2, Number = "A-1" });
        var indexes = host.Database.GetCollection<Order>("Orders").Indexes;
        await indexes.CreateOneAsync(new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Number)));
        await indexes.CreateOneAsync(new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));

        // Act
        var exception = await Assert.That(() => SyncAsync(host)).ThrowsExactly<MongoCommandException>();

        // Assert — the number's index as it was, refusing no duplicate.
        await Verify(new { exception!.CodeName, Indexes = await IndexesAsync(host.Database) });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingIndexesNoVaultDeclares_DropsThemLast()
    {
        // Arrange — an index of the orders no vault declares; one of a collection no vault declares; and the migrations'.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)), migrations => migrations.Add<Nothing>());
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Notes), new CreateIndexOptions { Sparse = true }));
        await host.Database.GetCollection<BsonDocument>("Archive").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("Number")));

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(new
        {
            Orders = await NamesAsync(host.Database),
            Archive = await NamesAsync(host.Database, "Archive"),
            Migrations = await NamesAsync(host.Database, "migrations"),
            Logs = Logged()
        });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingAnIndexDeclaredUnderAnotherName_RecreatesItUnderItsName()
    {
        // Arrange — the declared keys and options, named by hand.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Customer), new CreateIndexOptions { Name = "by_customer" }));

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(new { Indexes = await NamesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingIndexesAsDeclared_ChangesNothing()
    {
        // Arrange
        await using var host = Host(EveryOption);
        await SyncAsync(host);
        var indexes = await IndexesAsync(host.Database);
        var before = Logged().Count;

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(new { Unchanged = (await IndexesAsync(host.Database)).SequenceEqual(indexes), Logs = Logged().Skip(before) });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingACollectionTwoVaultsDeclare_KeepsEachVaultsIndexes()
    {
        // Arrange
        await using var host = TwoVaults(reports => reports.Index(keys => keys.Descending(x => x.PlacedAt)));
        await SyncAsync(host);
        var before = Logged().Count;

        // Act — again, as each start does.
        await SyncAsync(host);

        // Assert
        await Verify(new { Indexes = await NamesAsync(host.Database), Logs = Logged().Skip(before) });
    }

    [Test]
    public async Task MigrateAsync_SyncingOneOfTwoVaultsOfACollection_SetsUpWhatBothDeclare()
    {
        // Arrange — the reporting vault's index was there already.
        await using var host = TwoVaults(reports => reports.Index(keys => keys.Descending(x => x.PlacedAt)));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Descending(x => x.PlacedAt)));

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>()
            .MigrateAsync<StoreVault>(target: null, new MigrateOptions { SyncIndexes = true });

        // Assert
        await Assert.That(await NamesAsync(host.Database)).IsEquivalentTo(["_id_", "PlacedAt_-1", "Customer_1"]);
    }

    [Test]
    public async Task MigrateAllAsync_SyncingAnIndexTwoVaultsDeclareOtherwise_ThrowsChangingNothing()
    {
        // Arrange — the store's index of the customer is plain, the reporting vault's unique; and one no vault declares.
        await using var host = TwoVaults(reports => reports.Index(keys => keys.Ascending(x => x.Customer), index => index.Unique()));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));

        // Act
        var exception = await Assert.That(() => SyncAsync(host)).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Message = Scrubbed(exception!.Message), Indexes = await NamesAsync(host.Database) });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingASearchIndexOnAServerWithoutSearch_ThrowsChangingNothing()
    {
        // Arrange — the server has no search process; an index no vault declares.
        await using var host = Host(orders => orders.SearchIndex("default", index => index.Map(x => x.Notes, SearchField.String())));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));

        // Act
        var exception = await Assert.That(() => SyncAsync(host)).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(new { exception!.CodeName, Indexes = await NamesAsync(host.Database) });
    }

    [Test]
    public async Task MigrateAllAsync_SyncingACollectionThatsAView_LeavesIt()
    {
        // Arrange — the vault's orders a view of another collection, with an index of its own.
        await using var host = Host(_ => { });
        await host.Database.GetCollection<Order>("AllOrders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));
        await host.Database.CreateViewAsync<Order, Order>("Orders", "AllOrders", new EmptyPipelineDefinition<Order>());

        // Act
        await SyncAsync(host);

        // Assert
        await Assert.That(await NamesAsync(host.Database, "AllOrders")).IsEquivalentTo(["_id_", "Notes_1"]);
    }

    [Test]
    public async Task MigrateAllAsync_SyncingACollectionThatDeclaresNothing_DoesntCreateIt()
    {
        // Arrange
        await using var host = Host(_ => { });

        // Act
        await SyncAsync(host);

        // Assert
        await Assert.That(await host.Database.ListCollectionNames().ToListAsync()).IsEmpty();
    }

    [Test]
    public async Task MigrateAllAsync_SyncingIndexes_TracesEachChange()
    {
        // Arrange — unique by hand, declared plain; and one no vault declares.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number)));
        var indexes = host.Database.GetCollection<Order>("Orders").Indexes;
        await indexes.CreateOneAsync(new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Number),
            new CreateIndexOptions { Unique = true }));
        await indexes.CreateOneAsync(new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));
        using var spans = new ActivitySink();

        // Act
        await SyncAsync(host);

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    [NotInParallel("failCommand")] // The server's one failCommand failpoint, which a test setting it replaces for everyone.
    public async Task MigrateAllAsync_SyncingAndDroppingAnIndexFails_ThrowsTracingTheFailure()
    {
        // Arrange — an index no vault declares, whose drop the server fails.
        var name = $"drop-{Guid.NewGuid():N}";
        using var client = Mongo.CreateClient(name);
        await using var host = new VaultHost<StoreVault>(client, client.GetDatabase($"t{Guid.NewGuid():N}"),
            vault => vault.Collection(x => x.Orders, _ => { }));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));
        await Mongo.FailNextAsync("dropIndexes", name);
        using var spans = new ActivitySink();

        // Act
        var exception = await Assert.That(() => SyncAsync(host)).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(new
        {
            exception!.CodeName,
            Spans = spans.Snapshot().Where(span => span.Name == MongoFlowTelemetry.Activities.Index).Select(span => new { span.Status, span.Tags })
        });
    }

    private static Task SyncAsync(VaultHost<StoreVault> host) =>
        host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync(new MigrateOptions { SyncIndexes = true });

    // The store's vault, which indexes the orders by customer, and a reporting vault of the same orders, on one database.
    private VaultHost<StoreVault> TwoVaults(Action<IVaultCollectionBuilder<Order, int>> reports)
    {
        var database = Mongo.NewDatabase();
        return new VaultHost<StoreVault>(Mongo.Client,
            database,
            vault => vault.Collection(x => x.Orders, orders => orders.Index(keys => keys.Ascending(x => x.Customer))),
            services => services
                .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink))
                .AddMongoVault<ReportsVault>(vault => vault.UseDatabase(database).Collection(x => x.Orders, reports)));
    }
}
