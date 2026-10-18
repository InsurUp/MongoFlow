using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// The indexes a collection declares, which <see cref="IVaultMigrator"/> sets up after the vault's migrations:
/// <list type="number">
/// <item>those missing are created, with every option declared, and found as declared on the next run, though the server
/// lists them its own way;</item>
/// <item>an expiry or visibility that differs is changed in place;</item>
/// <item>one that differs in what MongoDB can't change in place, or one the server keeps under another name, fails the
/// migrator before any index is changed; one the vault doesn't declare is left;</item>
/// <item>each is traced under the migration, and the helpers for indexes managed by hand do the same; see
/// <c>IndexTests.Migrations.cs</c> for migrations applying them;</item>
/// <item>syncing indexes (<c>IndexTests.Sync.cs</c>), the indexes become what every vault declares of a collection: one
/// declared unique is made unique in place, unless documents share its keys; one that differs otherwise, or is declared
/// under another name, is recreated; those no vault declares are dropped, last, and one the server fails to drop fails
/// it, traced; two vaults declaring an index otherwise fail it, before any is changed; and a second run changes
/// nothing.</item>
/// </list>
/// </summary>
public partial class IndexTests
{
    private readonly LogSink _sink = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task MigrateAllAsync_IndexesDeclared_CreatesThem()
    {
        // Arrange
        await using var host = Host(EveryOption);

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Indexes = await IndexesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_IndexesAsDeclared_LeavesThem()
    {
        // Arrange
        await using var host = Host(EveryOption);
        await MigrateAsync(host);
        var before = Logged().Count;

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(Logged().Skip(before));
    }

    [Test]
    public async Task MigrateAllAsync_ExpiryAndVisibilityChanged_ChangesThemInPlace()
    {
        // Arrange — the same database, declared again with another expiry, hidden.
        await using var first = Host(orders => orders.Index(keys => keys.Ascending(x => x.ExpiresAt), index => index.ExpireAfter(TimeSpan.FromHours(1))));
        await MigrateAsync(first);
        await using var changed = Host(orders => orders.Index(keys => keys.Ascending(x => x.ExpiresAt),
            index => index.ExpireAfter(TimeSpan.FromHours(2)).Hidden()), database: first.Database);
        var before = Logged().Count;

        // Act
        await MigrateAsync(changed);

        // Assert
        await Verify(new { Indexes = await IndexesAsync(first.Database), Logs = Logged().Skip(before) });
    }

    [Test]
    public async Task MigrateAllAsync_DiffersInWhatCantChangeInPlace_ThrowsChangingNothing()
    {
        // Arrange — the number indexed by hand, not unique; the customer's index missing.
        await using var host = Host(orders => orders
            .Index(keys => keys.Ascending(x => x.Customer))
            .Index(keys => keys.Ascending(x => x.Number), index => index.Unique()));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Number)));

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Message = Scrubbed(exception!.Message), Indexes = await NamesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_AnIndexTheServerKeepsOneOfUnderAnotherName_Throws()
    {
        // Arrange — named by hand, hidden.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Customer), new CreateIndexOptions { Name = "by_customer", Hidden = true }));

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(Scrubbed(exception!.Message));
    }

    [Test]
    public async Task MigrateAllAsync_UniqueIndexOverDuplicates_FailsWithTheServersError()
    {
        // Arrange — two orders with one number.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number), index => index.Unique()));
        await host.SeedAsync("Orders", new Order { Id = 1, Number = "A-1" }, new Order { Id = 2, Number = "A-1" });
        using var spans = new ActivitySink();

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<MongoCommandException>();

        // Assert — the spans without the server's message, which names the index build.
        await Verify(new { exception!.Code, Spans = spans.Snapshot().Select(span => new { span.Name, span.Status, span.Tags }) });
    }

    [Test]
    public async Task MigrateAllAsync_IndexNotDeclared_LeavesIt()
    {
        // Arrange
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)));
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Notes)));

        // Act
        await MigrateAsync(host);

        // Assert
        await Assert.That(await NamesAsync(host.Database)).IsEquivalentTo(["_id_", "Notes_1", "Customer_1"]);
    }

    [Test]
    public async Task MigrateAllAsync_IndexesDeclared_TracesEachUnderTheMigration()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Host(orders => orders
            .Index(keys => keys.Ascending(x => x.Customer))
            .Index(keys => keys.Ascending(x => x.Number), index => index.Unique()));

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    public async Task CreateOrUpdateIndexAsync_IndexNotDeclared_CreatesChangesThenLeavesIt()
    {
        // Arrange
        await using var host = Host(_ => { });
        var orders = host.Vault.Orders.MongoCollection;

        // Act
        var created = await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.ExpiresAt),
            index => index.ExpireAfter(TimeSpan.FromHours(1)));
        var changed = await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.ExpiresAt),
            index => index.ExpireAfter(TimeSpan.FromHours(2)));
        var unchanged = await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.ExpiresAt),
            index => index.ExpireAfter(TimeSpan.FromHours(2)));

        // Assert
        await Verify(new { Changes = new[] { created, changed, unchanged }, Indexes = await IndexesAsync(host.Database) });
    }

    [Test]
    public async Task CreateOrUpdateIndexAsync_DiffersInWhatCantChangeInPlace_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Host(_ => { });
        var orders = host.Vault.Orders.MongoCollection;
        await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.Number));

        // Act & Assert
        await Assert.That(() => orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.Number), index => index.Unique()))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task RecreateIndexAsync_IndexNotDeclared_DropsItAndCreatesItAsDeclared()
    {
        // Arrange — not unique, and the same keys under another name.
        await using var host = Host(_ => { });
        var orders = host.Vault.Orders.MongoCollection;
        await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.Number));
        await orders.CreateOrUpdateIndexAsync(keys => keys.Ascending(x => x.Customer), index => index.Name("by_customer"));

        // Act
        await orders.RecreateIndexAsync(keys => keys.Ascending(x => x.Number), index => index.Unique());
        await orders.RecreateIndexAsync(keys => keys.Ascending(x => x.Customer));

        // Assert
        await Verify(await IndexesAsync(host.Database));
    }

    // Every option an index declares: unique; compound, descending; partial, expiring, named; text; hashed; with a
    // collation of its own; sparse, hidden.
    private static void EveryOption(IVaultCollectionBuilder<Order, int> orders) => orders
        .Index(keys => keys.Ascending(x => x.Number), index => index.Unique())
        .Index(keys => keys.Ascending(x => x.Customer).Descending(x => x.PlacedAt))
        .Index(keys => keys.Ascending(x => x.ExpiresAt), index => index
            .Name("expiring")
            .Partial(x => x.Status == OrderStatus.Closed)
            .ExpireAfter(TimeSpan.FromDays(30)))
        .Index(keys => keys.Text(x => x.Notes).Ascending(x => x.Customer))
        .Index(keys => keys.Hashed(x => x.Customer))
        .Index(keys => keys.Ascending(x => x.Customer), index => index
            .Name("by_customer_tr")
            .Collation(new Collation("tr", strength: CollationStrength.Secondary)))
        .Index(keys => keys.Ascending(x => x.Notes), index => index.Sparse().Hidden());

    private VaultHost<StoreVault> Host(Action<IVaultCollectionBuilder<Order, int>> orders,
        Action<IMigrationBuilder<StoreVault>>? migrations = null,
        IMongoDatabase? database = null) =>
        new(Mongo.Client,
            database ?? Mongo.NewDatabase(),
            vault =>
            {
                vault.Collection(x => x.Orders, orders);
                if (migrations is not null)
                {
                    vault.Migrations(migrations);
                }
            },
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));

    private static Task MigrateAsync(VaultHost<StoreVault> host) =>
        host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

    // What the migrator logged of indexes and collections, without what the index check logs in the background.
    private List<LogEntry> Logged() =>
    [
        .. _sink.Snapshot()
            .Where(entry => entry.Category is MongoFlowLogEvents.Indexes.Category or MongoFlowLogEvents.Collections.Category)
            .Where(entry => entry.EventId is not (>= MongoFlowLogEvents.Indexes.KeyIndexMissing and <= MongoFlowLogEvents.Indexes.IndexCheckCompleted))
    ];

    // The collection's indexes as the server lists them, without their version.
    private static async Task<List<BsonDocument>> IndexesAsync(IMongoDatabase database,
        string collection = "Orders")
    {
        var indexes = await (await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync()).ToListAsync();
        indexes.ForEach(index => index.Remove("v"));
        return indexes;
    }

    private static async Task<List<string>> NamesAsync(IMongoDatabase database,
        string collection = "Orders") =>
        [.. (await IndexesAsync(database, collection)).Select(index => index["name"].AsString)];

    private static string Scrubbed(string message) => Database().Replace(message, "{database}");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();
}
