using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class TransactionTests
{
    [Test]
    public async Task SaveAsync_FailingAfterItWrote_RollsTheOpenTransactionBack()
    {
        // Arrange — the first save goes through; the second fails after its write.
        var failure = new ArmedFailure();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(failure));
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        failure.AfterWriting = true;
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act & Assert
        await ThrowsTask(() => transaction.CommitAsync()).IgnoreStackTrace();
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }

    [Test]
    public async Task SaveAsync_RejectedByTheServerInAnOpenTransaction_RollsItBack()
    {
        // Arrange — the second save repeats a key the first wrote.
        await using var host = Mongo.Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Act
        var exception = await Assert.That(() => transaction.CommitAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Assert.That(exception!.InnerException).IsTypeOf<ClientBulkWriteException>();
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }

    [Test]
    public async Task SaveAsync_InATransactionAFailedSaveRolledBack_FailsWithoutWriting()
    {
        // Arrange
        var failure = new ArmedFailure { AfterWriting = true };
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(failure));
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
        failure.AfterWriting = false;
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert — the save failed inside the dead transaction rather than running, and committing, on its own.
        await Assert.That(exception!.Message).StartsWith("The transaction was rolled back");
        await Assert.That(host.Transactions.Current).IsSameReferenceAs(transaction);
        await Assert.That(await host.StoredAsync("Orders")).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_InATransactionAFailedSaveRolledBack_Fails()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new ArmedFailure { AfterWriting = true }));
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act & Assert
        var exception = await Assert.That(async () => await host.Vault.Orders.QueryAsync()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(exception!.Message).StartsWith("The transaction was rolled back");
    }

    [Test]
    public async Task DisposeAsync_TransactionAFailedSaveRolledBack_LetsTheNextSaveRunOnItsOwn()
    {
        // Arrange
        var failure = new ArmedFailure { AfterWriting = true };
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(failure));
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act
        await transaction.DisposeAsync();
        failure.AfterWriting = false;
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(host.Transactions.Current).IsNull();
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SaveAsync_FailingBeforeItWrote_LeavesTheOpenTransactionUsable()
    {
        // Arrange
        var failure = new ArmedFailure { BeforeWriting = true };
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(failure));
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
        failure.BeforeWriting = false;
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(await host.StoredAsync("Orders"));
    }

    [Test]
    public async Task SaveAsync_FailingAfterItWrote_RunsEveryJoinedSavesFailureHooksOnceNewestFirst()
    {
        // Arrange — registered first, so the recorder's hooks after the write run after the failure's.
        var log = new HookLog();
        var failure = new ArmedFailure();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new RecordingInterceptor("shop", log))
            .AddInterceptor(failure));
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        failure.AfterWriting = true;
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act
        await transaction.DisposeAsync();

        // Assert
        await Verify(log.Entries);
    }

    [Test]
    public async Task SaveAsync_AnInterceptorsSaveFailingAfterItWrote_WritesNeither()
    {
        // Arrange — the ledger's save, made from inside the shop's, fails after its write.
        await using var host = HostWithLedger(out _,
            shop => shop.AddInterceptor<LedgerWriter>(),
            entries => entries.AddInterceptor(new FailingAfterTheWrite()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Orders = await host.StoredAsync("Orders"), Entries = await host.StoredAsync("Entries") })
            .DontIgnoreEmptyCollections();
    }
}
