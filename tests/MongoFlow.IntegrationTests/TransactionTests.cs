using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Saves and the scope's transaction:
/// <list type="number">
/// <item>a save joins the open transaction, and is written only when it commits, together with every other save that
/// joined;</item>
/// <item>without one, a save runs in a transaction of its own, which its interceptors' saves join;</item>
/// <item>a transaction runs <see cref="VaultInterceptor.CommittedAsync"/> after its commit and
/// <see cref="VaultInterceptor.FailedAsync"/>, newest save first, when it's rolled back or its commit fails;</item>
/// <item>a vault on another client can't join, and a save that fails to join runs no hooks;</item>
/// <item>a scope's saves run one after another, and a transaction still open when its scope ends is rolled back; see
/// <c>TransactionTests.Scope.cs</c>;</item>
/// <item>a save that fails after writing rolls the open transaction back whole, once, which then fails whatever uses it
/// until it's disposed, a save its interceptors carry on with included; one that fails before writing leaves it usable.
/// See <c>TransactionTests.FailedSaves.cs</c>.</item>
/// </list>
/// </summary>
public partial class TransactionTests
{
    // The server has one failCommand failpoint: a test setting it replaces what another set.
    private const string FailCommand = "failCommand";

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_InATransaction_IsWrittenWhenItCommits()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        var before = await host.StoredAsync("Orders");

        // Act
        await transaction.CommitAsync();

        // Assert
        await Assert.That(before).IsEmpty();
        await Assert.That(await host.StoredAsync("Orders")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SaveAsync_InATransactionRolledBack_IsNotWritten()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }

    [Test]
    public async Task SaveAsync_InATransactionDisposedWithoutCommitting_IsNotWritten()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.DisposeAsync();

        // Assert
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveAsync_TwoVaultsInOneTransaction_AreWrittenTogetherOrNotAtAll(bool commit)
    {
        // Arrange
        await using var host = HostWithLedger(out var ledger);
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        ledger().Entries.Add(new LedgerEntry { Id = 1, Text = "order 1" });
        await host.Vault.SaveAsync();
        await ledger().SaveAsync();

        // Act
        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        // Assert
        await Verify(new { Orders = await host.StoredAsync("Orders"), Entries = await host.StoredAsync("Entries") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_WithoutATransaction_RunsInItsOwnUntilItEnds()
    {
        // Arrange
        var probe = new TransactionProbe();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(probe));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { probe.CurrentWhileSaving, probe.SessionInTransaction, OpenAfter = host.Transactions.Current is not null });
    }

    [Test]
    public async Task SaveAsync_OfAnotherVaultFromAnInterceptor_IsWrittenWithTheSave()
    {
        // Arrange
        await using var host = HostWithLedger(out _, vault => vault.AddInterceptor<LedgerWriter>());
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Orders = await host.StoredAsync("Orders"), Entries = await host.StoredAsync("Entries") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_FailingAfterAnInterceptorSavedAnotherVault_WritesNeither()
    {
        // Arrange — registered first, so its hook after the write runs after the ledger writer's.
        await using var host = HostWithLedger(out _, vault => vault
            .AddInterceptor(new FailingAfterTheWrite())
            .AddInterceptor<LedgerWriter>());
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Orders = await host.StoredAsync("Orders"), Entries = await host.StoredAsync("Entries") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_InATransactionOnAnotherClient_ThrowsInvalidOperationException()
    {
        // Arrange — the transaction starts on the registered client; the vault uses another.
        using var other = Mongo.CreateClient("other-client");
        await using var host = new VaultHost<ShopVault>(Mongo.Client, other.GetDatabase($"t{Guid.NewGuid():N}"));
        await using var transaction = await host.Transactions.BeginAsync();
        _ = transaction.Session;
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task CommitAsync_JoinedSave_RunsCommittedOnlyThen()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)));
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        var beforeCommit = log.Entries.ToList();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new { BeforeCommit = beforeCommit, AfterCommit = log.Entries });
    }

    [Test]
    public async Task RollbackAsync_JoinedSaves_RunsFailedNewestFirst()
    {
        // Arrange
        var log = new HookLog();
        await using var host = HostWithLedger(out var ledger,
            vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)),
            vault => vault.AddInterceptor(new RecordingInterceptor("ledger", log)));
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        ledger().Entries.Add(new LedgerEntry { Id = 1, Text = "order 1" });
        await ledger().SaveAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Verify(log.Entries);
    }

    [Test]
    [NotInParallel(FailCommand)]
    public async Task CommitAsync_ServerRejectsIt_RunsFailedAndThrows()
    {
        // Arrange
        var name = $"commit-{Guid.NewGuid():N}";
        using var client = Mongo.CreateClient(name);
        var log = new HookLog();
        await using var host = new VaultHost<ShopVault>(client, client.GetDatabase($"t{Guid.NewGuid():N}"),
            vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)));
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        await Mongo.FailNextAsync("commitTransaction", name);

        // Act
        await Assert.That(() => transaction.CommitAsync()).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(new { log.Entries, Stored = await host.StoredAsync("Orders") }).DontIgnoreEmptyCollections();
    }

    [Test]
    [NotInParallel(FailCommand)]
    public async Task SaveAsync_ItsOwnCommitRejected_RunsFailedAndThrows()
    {
        // Arrange
        var name = $"commit-{Guid.NewGuid():N}";
        using var client = Mongo.CreateClient(name);
        var log = new HookLog();
        await using var host = new VaultHost<ShopVault>(client, client.GetDatabase($"t{Guid.NewGuid():N}"),
            vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Mongo.FailNextAsync("commitTransaction", name);

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(new { log.Entries, Stored = await host.StoredAsync("Orders") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Session_BeforeAnyVaultJoins_CarriesDriverWritesWithTheSaves()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        await host.Database.GetCollection<BsonDocument>("Notes").InsertOneAsync(transaction.Session, new BsonDocument("_id", "written by the driver"));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new { Notes = await host.StoredAsync("Notes"), Orders = await host.StoredAsync("Orders") });
    }

    /// <summary>A shop vault and a ledger vault on one database and one provider, as one app would register them.</summary>
    private VaultHost<ShopVault> HostWithLedger(out Func<LedgerVault> ledger,
        Action<IVaultBuilder<ShopVault>>? shop = null,
        Action<IVaultBuilder<LedgerVault>>? entries = null)
    {
        var database = Mongo.NewDatabase();
        var host = new VaultHost<ShopVault>(Mongo.Client, database, shop, services => services.AddMongoVault<LedgerVault>(vault =>
        {
            vault.UseDatabase(database);
            entries?.Invoke(vault);
        }));

        ledger = () => host.Services.GetRequiredService<LedgerVault>();
        return host;
    }
}
