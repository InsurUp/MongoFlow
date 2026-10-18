using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// The settings a collection declares, which <see cref="IVaultMigrator"/> sets up before the vault's migrations:
/// <list type="number">
/// <item>a missing collection is created with them, before any migration writes to it, and found as declared on the
/// next run, its indexes with its collation included;</item>
/// <item>a collection declaring only that it's created is created as the server would, once, and one that exists is left
/// as it is, whatever its settings;</item>
/// <item>a validator, change stream images and expiry that differ are changed in place;</item>
/// <item>a collation that differs fails the migrator before any collection is changed;</item>
/// <item>a clustered collection is saved to in a transaction, and a collection a migration drops is created anew with its
/// settings, by the migration or before its indexes are.</item>
/// </list>
/// </summary>
public partial class CollectionSettingsTests
{
    private readonly LogSink _sink = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task MigrateAllAsync_SettingsDeclared_CreatesTheCollectionWithThem()
    {
        // Arrange
        await using var host = Host(payments => payments.Settings(settings => settings
            .Collation(new Collation("tr", strength: CollationStrength.Secondary))
            .Validator(x => x.Amount > 0, DocumentValidationLevel.Moderate)
            .ChangeStreamPreAndPostImages()));

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Options = await OptionsAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_CreateCollectionDeclared_CreatesTheCollectionOnce()
    {
        // Arrange — no other setting, and no index, which would create it too.
        await using var host = Host(payments => payments.Settings(settings => settings.CreateCollection()));

        // Act — twice: the second run finds it.
        await MigrateAsync(host);
        await MigrateAsync(host);

        // Assert
        await Verify(new { Options = await OptionsAsync(host.Database), Logs = Logged() }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task MigrateAllAsync_CreateCollectionDeclaredOnOneThatExists_LeavesIt()
    {
        // Arrange — created by hand, with settings the vault doesn't declare.
        await using var host = Host(payments => payments.Settings(settings => settings.CreateCollection()));
        await host.Database.CreateCollectionAsync("Payments", new CreateCollectionOptions
        {
            Collation = new Collation("tr"),
            ChangeStreamPreAndPostImagesOptions = new ChangeStreamPreAndPostImagesOptions { Enabled = true }
        });

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Options = await OptionsAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_AMigrationWritesWhatTheValidatorRefuses_Fails()
    {
        // Arrange
        await using var host = Host(payments => payments.Settings(settings => settings.Validator(x => x.Amount > 0)),
            migrations => migrations.Add<SaveARefund>());

        // Act & Assert
        await Assert.That(() => MigrateAsync(host)).ThrowsExactly<MigrationFailedException>();
    }

    [Test]
    public async Task MigrateAllAsync_SettingsAsDeclared_LeavesThem()
    {
        // Arrange — an index too, which has the collection's collation.
        await using var host = Host(payments => payments
            .Settings(settings => settings
                .Collation(new Collation("tr", strength: CollationStrength.Secondary))
                .Validator(x => x.Amount > 0)
                .Clustered(TimeSpan.FromDays(1)))
            .Index(keys => keys.Ascending(x => x.Payer)));
        await MigrateAsync(host);
        var before = Logged().Count;

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(Logged().Skip(before));
    }

    [Test]
    public async Task MigrateAllAsync_ValidatorImagesAndExpiryChanged_ChangesThemInPlace()
    {
        // Arrange — the same database, declared again otherwise.
        await using var first = Host(payments => payments.Settings(settings => settings
            .Validator(x => x.Amount > 0)
            .Clustered(TimeSpan.FromDays(1))));
        await MigrateAsync(first);
        await using var changed = Host(payments => payments.Settings(settings => settings
                .Validator(x => x.Amount >= 0, action: DocumentValidationAction.Warn)
                .ChangeStreamPreAndPostImages()
                .Clustered()),
            database: first.Database);
        var before = Logged().Count;

        // Act
        await MigrateAsync(changed);

        // Assert
        await Verify(new { Options = await OptionsAsync(first.Database), Logs = Logged().Skip(before) });
    }

    [Test]
    public async Task MigrateAllAsync_CollationDiffers_ThrowsChangingNothing()
    {
        // Arrange — created by hand, without a collation.
        await using var host = Host(payments => payments.Settings(settings => settings
            .Collation(new Collation("tr"))
            .Validator(x => x.Amount > 0)));
        await host.Database.CreateCollectionAsync("Payments");

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Message = Scrubbed(exception!.Message), Options = await OptionsAsync(host.Database), Logs = Logged() })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_ClusteredCollection_SavesInItsTransaction()
    {
        // Arrange
        await using var host = Host(payments => payments.Settings(settings => settings.Clustered()));
        await MigrateAsync(host);
        host.Vault.Payments.Add(new Payment { Id = 1, Payer = "Ada", Amount = 5 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Payments"));
    }

    [Test]
    public async Task CreateAsync_InAMigration_CreatesTheCollectionAnewWithItsSettings()
    {
        // Arrange
        await using var host = Host(payments => payments.Settings(settings => settings.Validator(x => x.Amount > 0).Clustered()),
            migrations => migrations.Add<RecreatePayments>());

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Options = await OptionsAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task MigrateAllAsync_CollectionAMigrationDropped_IsCreatedWithItsSettingsBeforeItsIndexes()
    {
        // Arrange
        await using var host = Host(payments => payments
                .Settings(settings => settings.Collation(new Collation("tr")))
                .Index(keys => keys.Ascending(x => x.Payer)),
            migrations => migrations.Add<DropPayments>());

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Options = await OptionsAsync(host.Database), Logs = Logged() });
    }

    private VaultHost<LedgerVault> Host(Action<IVaultCollectionBuilder<Payment, int>> payments,
        Action<IMigrationBuilder<LedgerVault>>? migrations = null,
        IMongoDatabase? database = null) =>
        new(Mongo.Client,
            database ?? Mongo.NewDatabase(),
            vault =>
            {
                vault.Collection(x => x.Payments, payments);
                if (migrations is not null)
                {
                    vault.Migrations(migrations);
                }
            },
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));

    private static Task MigrateAsync(VaultHost<LedgerVault> host) =>
        host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

    // What the migrator logged of collections and indexes, without what the index check logs in the background.
    private List<LogEntry> Logged() =>
    [
        .. _sink.Snapshot()
            .Where(entry => entry.Category is MongoFlowLogEvents.Indexes.Category or MongoFlowLogEvents.Collections.Category)
            .Where(entry => entry.EventId is not (>= MongoFlowLogEvents.Indexes.KeyIndexMissing and <= MongoFlowLogEvents.Indexes.IndexCheckCompleted))
    ];

    // The collection's options as the server lists them.
    private static async Task<BsonDocument> OptionsAsync(IMongoDatabase database)
    {
        var options = new ListCollectionsOptions { Filter = new BsonDocument("name", "Payments") };
        var listed = await (await database.ListCollectionsAsync(options)).SingleAsync();
        return listed["options"].AsBsonDocument;
    }

    private static string Scrubbed(string message) => Database().Replace(message, "{database}");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();
}
