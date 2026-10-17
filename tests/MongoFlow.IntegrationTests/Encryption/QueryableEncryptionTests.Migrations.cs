using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Encryption;

namespace MongoFlow.IntegrationTests.Encryption;

public partial class QueryableEncryptionTests
{
    [Test]
    public async Task MigrateAllAsync_NewDatabase_CreatesTheEncryptedCollectionsAndTheirKeys()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await using var host = PlainHost(database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Schema = await EncryptedFieldsAsync(database, "Customers"),
            Keys = await database.GetCollection<BsonDocument>("__keyVault").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Encryption.Category)
        });
    }

    [Test]
    public async Task MigrateAllAsync_EncryptedAsDeclared_LeavesItAsItIs()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await using var host = PlainHost(database);
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();
        await migrator.MigrateAllAsync();

        // Act
        await migrator.MigrateAllAsync();

        // Assert
        await Verify(_sink.Snapshot(MongoFlowLogEvents.Encryption.Category));
    }

    [Test]
    public async Task MigrateAllAsync_CollectionHoldingDocuments_RebuildsItEncrypted()
    {
        // Arrange — the documents and indexes as an app stored them before encrypting; a unique index can't cover an
        // encrypted member, so it's left behind.
        var database = Mongo.NewDatabase();
        var plain = database.GetCollection<BsonDocument>("Customers");
        await plain.InsertManyAsync(Customers(3).Select(customer => customer.ToBsonDocument()));
        await plain.Indexes.CreateManyAsync([
            new CreateIndexModel<BsonDocument>(new BsonDocument("Name", 1), new CreateIndexOptions { Name = "byName" }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("Ssn", 1), new CreateIndexOptions { Name = "bySsn", Unique = true }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("Version", 1), new CreateIndexOptions<BsonDocument>
            {
                Name = "versionsOfThoseWithAnSsn",
                PartialFilterExpression = BsonDocument.Parse("{ $and: [{ Version: { $gte: 0 } }, { Ssn: { $exists: true } }] }")
            })
        ]);
        await using var host = PlainHost(database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Stored = await host.StoredAsync("Customers"),
            Read = await (await host.Vault.Customers.FindAsync(x => x.Ssn == "222-22")).Project(x => x.Name).ToListAsync(),
            Indexes = (await (await plain.Indexes.ListAsync()).ToListAsync()).Select(index => index["name"]),
            Unencrypted = await host.StoredAsync("Customers.unencrypted"),
            Collections = (await (await database.ListCollectionNamesAsync()).ToListAsync()).Order(),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Encryption.Category)
        });
    }

    [Test]
    public async Task MigrateAllAsync_InterruptedRebuild_GoesOnWhereItStopped()
    {
        // Arrange — a rebuild renamed the collection and copied the first customer before it stopped.
        var database = Mongo.NewDatabase();
        await using (var interrupted = PlainHost(database))
        {
            await interrupted.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
            interrupted.Vault.Customers.Add(Customers(1).Single());
            await interrupted.Vault.SaveAsync();
        }

        await database.GetCollection<BsonDocument>("Customers.encrypting")
            .InsertManyAsync(Customers(3).Select(customer => customer.ToBsonDocument()));
        await using var host = PlainHost(database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Read = await (await host.Vault.Customers.FindAsync(x => x.Salary > 0)).SortBy(x => x.Id).Project(x => x.Ssn).ToListAsync(),
            Collections = (await (await database.ListCollectionNamesAsync()).ToListAsync()).Order(),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Encryption.Category)
        });
    }

    [Test]
    public async Task MigrateAllAsync_EncryptedOtherwise_ThrowsInvalidOperationException()
    {
        // Arrange — the collection was created when Ssn was queried and Notes wasn't; the vault now declares the opposite,
        // and encrypts Name too.
        var database = Mongo.NewDatabase();
        await using (var before = PlainHost(database))
        {
            await before.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
        }

        await using var host = PlainHost(database, customers => customers
            .Encrypt(x => x.Ssn)
            .Encrypt(x => x.Salary, EncryptedQuery.Range(0, 1_000_000))
            .Encrypt(x => x.Notes, EncryptedQuery.Equality)
            .Encrypt(x => x.Name));

        // Act & Assert
        await ThrowsTask(() => host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task MigrateAllAsync_AfterARebuild_WarnsTheUnencryptedCopyRemains()
    {
        // Arrange
        var database = Mongo.NewDatabase();
        await database.GetCollection<BsonDocument>("Customers").InsertManyAsync(Customers(1).Select(customer => customer.ToBsonDocument()));
        await using var host = PlainHost(database);
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();
        await migrator.MigrateAllAsync();
        var rebuilt = _sink.Snapshot(MongoFlowLogEvents.Encryption.Category).Count;

        // Act
        await migrator.MigrateAllAsync();

        // Assert
        await Verify(_sink.Snapshot(MongoFlowLogEvents.Encryption.Category).Skip(rebuilt));
    }

    [Test]
    public async Task MigrateAllAsync_InterruptedAfterCopying_FinishesTheRebuild()
    {
        // Arrange — the rebuild had copied every customer when it stopped.
        var database = Mongo.NewDatabase();
        await using (var interrupted = PlainHost(database))
        {
            await interrupted.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
            interrupted.Vault.Customers.AddRange(Customers(2));
            await interrupted.Vault.SaveAsync();
        }

        await database.GetCollection<BsonDocument>("Customers.encrypting")
            .InsertManyAsync(Customers(2).Select(customer => customer.ToBsonDocument()));
        await using var host = PlainHost(database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Read = await (await host.Vault.Customers.FindAsync(x => x.Salary > 0)).CountDocumentsAsync(),
            Collections = (await (await database.ListCollectionNamesAsync()).ToListAsync()).Order()
        });
    }

    [Test]
    public async Task MigrateAllAsync_CollectionWrittenDuringARebuild_ThrowsInvalidOperationException()
    {
        // Arrange — something wrote to the collection while a rebuild was copying it, creating it unencrypted.
        var database = Mongo.NewDatabase();
        await database.GetCollection<BsonDocument>("Customers.encrypting").InsertOneAsync(new BsonDocument("_id", 1));
        await database.GetCollection<BsonDocument>("Customers").InsertOneAsync(new BsonDocument("_id", 2));
        await using var host = PlainHost(database);

        // Act & Assert
        await ThrowsTask(() => host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task MigrateAllAsync_UnencryptedCopyThereAlready_ThrowsInvalidOperationException()
    {
        // Arrange — an earlier rebuild's copy is still there, under the name this one needs.
        var database = Mongo.NewDatabase();
        await database.GetCollection<BsonDocument>("Customers").InsertOneAsync(new BsonDocument("_id", 1));
        await database.GetCollection<BsonDocument>("Customers.unencrypted").InsertOneAsync(new BsonDocument("_id", 1));
        await using var host = PlainHost(database);

        // Act
        var exception = await Assert.That(() => host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync())
            .ThrowsExactly<InvalidOperationException>();

        // Assert — the server's error inside names the connection, which differs each run.
        await Verify(exception!.Message);
    }

    [Test]
    public async Task MigrateAllAsync_ThousandsOfDocuments_CopiesThemInBatches()
    {
        // Arrange
        const int Count = 2500;
        var database = Mongo.NewDatabase();
        await database.GetCollection<BsonDocument>("Customers").InsertManyAsync(Enumerable.Range(1, Count)
            .Select(id => new Customer { Id = id, Name = $"Customer {id}", Ssn = $"{id:D5}", Salary = id }.ToBsonDocument()));
        await using var host = PlainHost(database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(new
        {
            Copied = await database.GetCollection<BsonDocument>("Customers").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),
            Last = (await host.Vault.Customers.FirstOrDefaultAsync(x => x.Ssn == "02500"))?.Name,
            Progress = _sink.Snapshot(MongoFlowLogEvents.Encryption.Category)
                .Where(entry => entry.EventId == MongoFlowLogEvents.Encryption.RebuildProgress)
                .Select(entry => entry.Message)
        });
    }

    [Test]
    public async Task SaveAsync_BeforeMigrating_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = PlainHost(Mongo.NewDatabase());
        host.Vault.Customers.Add(new Customer { Id = 1, Ssn = "111-11" });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task SaveAsync_VaultsWithTheSameEncryption_SaveInOneTransaction()
    {
        // Arrange — the suppliers' vault encrypts too, and the ledger's encrypts nothing, with the same settings: they
        // share the client that encrypts, which a transaction needs.
        var database = Mongo.NewDatabase();
        var name = database.DatabaseNamespace.DatabaseName;
        var services = new ServiceCollection();
        services.AddSingleton(Mongo.Client);
        services.AddMongoVault<CustomerVault>(vault =>
        {
            vault.UseDatabase(database);
            UseEncryption(vault, name);
            vault.Collection(x => x.Customers, Encrypted);
        });
        services.AddMongoVault<SupplierVault>(vault =>
        {
            vault.UseDatabase(database);
            UseEncryption(vault, name);
            vault.Collection(x => x.Suppliers, suppliers => suppliers.Encrypt(x => x.TaxNumber, EncryptedQuery.Equality));
        });
        services.AddMongoVault<LedgerVault>(vault =>
        {
            vault.UseDatabase(database);
            UseEncryption(vault, name);
        });
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await using (var transaction = await scope.ServiceProvider.GetRequiredService<IVaultTransactionManager>().BeginAsync())
        {
            var customers = scope.ServiceProvider.GetRequiredService<CustomerVault>();
            var suppliers = scope.ServiceProvider.GetRequiredService<SupplierVault>();
            var ledger = scope.ServiceProvider.GetRequiredService<LedgerVault>();

            customers.Customers.Add(new Customer { Id = 1, Ssn = "111-11" });
            suppliers.Suppliers.Add(new Supplier { Id = 1, TaxNumber = "TR-1" });
            ledger.Entries.Add(new Entry { Id = 1, Text = "both added" });
            await customers.SaveAsync();
            await suppliers.SaveAsync();
            await ledger.SaveAsync();
            await transaction.CommitAsync();
        }

        // Assert
        var read = scope.ServiceProvider.GetRequiredService<SupplierVault>();
        await Verify(new
        {
            Supplier = await read.Suppliers.FirstOrDefaultAsync(x => x.TaxNumber == "TR-1"),
            Customers = await database.GetCollection<BsonDocument>("Customers").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty),
            Entries = await database.GetCollection<BsonDocument>("Entries").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)
        });
    }

    /// <summary>
    /// A <see cref="CustomerVault"/> with the customers encrypted on <paramref name="database"/>, which nothing has set up,
    /// logging encryption to the sink.
    /// </summary>
    private VaultHost<CustomerVault> PlainHost(IMongoDatabase database,
        Action<IVaultCollectionBuilder<Customer, int>>? customers = null,
        Action<IMigrationBuilder<CustomerVault>>? migrations = null) =>
        new(Mongo.Client, database, vault =>
            {
                UseEncryption(vault, database.DatabaseNamespace.DatabaseName);
                vault.Collection(x => x.Customers, customers ?? Encrypted);
                if (migrations is not null)
                {
                    vault.Migrations(migrations);
                }
            },
            services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));

    /// <summary>The fields <paramref name="collection"/> is encrypted with, as the server holds them, data keys aside.</summary>
    private static async Task<BsonArray> EncryptedFieldsAsync(IMongoDatabase database,
        string collection)
    {
        var options = new ListCollectionsOptions { Filter = new BsonDocument("name", collection) };
        var stored = await (await database.ListCollectionsAsync(options)).SingleAsync();
        var fields = stored["options"]["encryptedFields"]["fields"].AsBsonArray;

        foreach (var field in fields.Select(field => field.AsBsonDocument))
        {
            field["keyId"] = field["keyId"].IsGuid ? "(data key)" : field["keyId"];
        }

        return fields;
    }
}
