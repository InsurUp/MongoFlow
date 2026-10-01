using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Semver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="IVaultMigrator"/> applying vaults' migrations:
/// <list type="number">
/// <item>pending migrations run oldest first, each in a transaction the vault's saves join unless it opts out, and are
/// recorded in that transaction;</item>
/// <item>a target stops short, and one below the applied versions reverts the migrations above it, newest first;</item>
/// <item>a migration added below the current version still runs, and the history earlier MongoFlow versions wrote
/// counts;</item>
/// <item>every registered vault is migrated, each recording in a collection of its own;</item>
/// <item>a migration that fails, or leaves writes unsaved, rolls back what it did in its transaction and isn't recorded;
/// see <c>MigrationTests.Failures.cs</c>.</item>
/// </list>
/// </summary>
public partial class MigrationTests
{
    private readonly StepLog _steps = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly LogSink _sink = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task MigrateAsync_PendingMigrations_AppliesThemOldestFirstAndRecordsEach()
    {
        // Arrange — declared out of order.
        await using var host = Host(m => m.Add<IndexNames>().Add<SeedEntries>().Add<ActivateEntries>());

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(new
        {
            _steps.Entries,
            Stored = await host.StoredAsync("Entries"),
            History = await HistoryAsync(host),
            Indexes = await IndexesAsync(host)
        });
    }

    [Test]
    public async Task MigrateAsync_Target_AppliesUpToIt()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>().Add<IndexNames>());
        var migrator = Migrator(host);

        // Act
        await migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 1, 0));

        // Assert
        await Verify(new { _steps.Entries, Version = (await migrator.GetVersionAsync<RegistryVault>())?.ToString() });
    }

    [Test]
    public async Task MigrateAsync_TargetBelowTheAppliedVersions_RevertsTheMigrationsAboveItNewestFirst()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>().Add<IndexNames>());
        var migrator = Migrator(host);
        await migrator.MigrateAsync<RegistryVault>();
        _steps.Entries.Clear();

        // Act
        await migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 0, 0));

        // Assert
        await Verify(new
        {
            _steps.Entries,
            Stored = await host.StoredAsync("Entries"),
            History = await HistoryAsync(host),
            Indexes = await IndexesAsync(host)
        });
    }

    [Test]
    public async Task MigrateAsync_MigrationAddedBelowTheCurrentVersion_AppliesIt()
    {
        // Arrange — a release applied 1.0.0 and 2.0.0; the next one adds 1.1.0.
        var database = Mongo.NewDatabase();
        await using (var before = Host(m => m.Add<SeedEntries>().Add<IndexNames>(), database))
        {
            await Migrator(before).MigrateAsync<RegistryVault>();
        }

        _steps.Entries.Clear();
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>().Add<IndexNames>(), database);

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(new { _steps.Entries, History = await HistoryAsync(host) });
    }

    [Test]
    public async Task MigrateAsync_HistoryAnEarlierVersionWrote_CountsItsMigrationsAsApplied()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>());
        await host.SeedAsync("migrations", new BsonDocument
        {
            ["_id"] = ObjectId.Parse("5f0c0ffee000000000001234"),
            ["Version"] = "1.0.0",
            ["Name"] = "SeedEntries",
            ["Description"] = BsonNull.Value,
            ["Timestamp"] = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(new { _steps.Entries, History = await HistoryAsync(host) });
    }

    [Test]
    public async Task MigrateAsync_WithAndWithoutATransaction_GivesTheMigrationTheVaultsDatabaseAndASession()
    {
        // Arrange
        await using var host = Host(m => m.Add<ProbeInTransaction>().Add<ProbeOutsideTransaction>());

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(_steps.Entries);
    }

    [Test]
    public async Task MigrateAsync_CollectionName_RecordsTheHistoryThere()
    {
        // Arrange
        const string History = "registry_migrations";
        await using var host = Host(m => m.Add<SeedEntries>().CollectionName(History));

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(new
        {
            History = await HistoryAsync(host, History),
            Collections = (await (await host.Database.ListCollectionNamesAsync()).ToListAsync()).Order()
        });
    }

    [Test]
    public async Task MigrateAllAsync_TwoVaultsOnOneDatabase_MigratesEachInItsOwnHistory()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await using var provider = Services()
            .AddMongoVault<RegistryVault>(vault => vault.UseDatabase(database).Migrations(m => m.Add<SeedEntries>()))
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(database).Migrations(m => m.Add<SeedOrders>().CollectionName("shop_migrations")))
            .BuildServiceProvider();

        // Act
        await provider.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            _steps.Entries,
            Registry = await HistoryAsync(database, "migrations"),
            Shop = await HistoryAsync(database, "shop_migrations")
        });
    }

    [Test]
    public async Task MigrateAsync_WithoutATimeProvider_RecordsTheSystemTime()
    {
        // Arrange
        await using var host = new VaultHost<RegistryVault>(Mongo.Client, Mongo.NewDatabase(),
            vault => vault.Migrations(m => m.Add<SeedEntries>()), services => services.AddSingleton(_steps));
        var before = DateTime.UtcNow;

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert — BSON dates keep milliseconds, so the recorded time can fall just short of before.
        var recorded = (await host.StoredAsync("migrations")).Single()["Timestamp"].ToUniversalTime();
        await Assert.That(recorded).IsBetween(before.AddMilliseconds(-1), DateTime.UtcNow);
    }

    [Test]
    public async Task GetVersionAsync_NothingApplied_ReturnsNull()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>());

        // Act
        var version = await Migrator(host).GetVersionAsync<RegistryVault>();

        // Assert
        await Assert.That(version).IsNull();
    }

    [Test]
    public async Task MigrateAsync_AppliedAndThenUpToDate_LogsEachMigrationAndThatNothingIsLeft()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>());
        var migrator = Migrator(host);

        // Act
        await migrator.MigrateAsync<RegistryVault>();
        await migrator.MigrateAsync<RegistryVault>();

        // Assert
        await Verify(_sink.Snapshot(MongoFlowLogEvents.Migrations.Category));
    }

    private VaultHost<RegistryVault> Host(Action<IMigrationBuilder<RegistryVault>> migrations,
        IMongoDatabase? database = null) =>
        new(Mongo.Client, database ?? Mongo.NewDatabase(), vault => vault.Migrations(migrations), services => Register(services));

    private IServiceCollection Services() => Register(new ServiceCollection().AddSingleton(Mongo.Client));

    private IServiceCollection Register(IServiceCollection services) => services
        .AddSingleton(_steps)
        .AddSingleton<TimeProvider>(_clock)
        .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink));

    private static IVaultMigrator Migrator(VaultHost<RegistryVault> host) => host.Services.GetRequiredService<IVaultMigrator>();

    private static Task<List<object>> HistoryAsync(VaultHost<RegistryVault> host,
        string collection = "migrations") =>
        HistoryAsync(host.Database, collection);

    // The records without their generated ids.
    private static async Task<List<object>> HistoryAsync(IMongoDatabase database,
        string collection) =>
        (await database.GetCollection<BsonDocument>(collection)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync())
        .Select(object (record) => new
        {
            Version = record["Version"].AsString,
            Name = record["Name"].AsString,
            Description = record["Description"].IsBsonNull ? null : record["Description"].AsString,
            Timestamp = record["Timestamp"].ToUniversalTime()
        })
        .ToList();

    private static async Task<List<string>> IndexesAsync(VaultHost<RegistryVault> host) =>
        (await (await host.Database.GetCollection<BsonDocument>("Entries").Indexes.ListAsync()).ToListAsync())
        .Select(index => index["name"].AsString)
        .ToList();
}
