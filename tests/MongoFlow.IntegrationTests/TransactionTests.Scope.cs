using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.IntegrationTests;

public partial class TransactionTests
{
    [Test]
    public async Task SaveAsync_AnotherVaultsWhileOneRuns_ThrowsAndLeavesTheFirstToFinish()
    {
        // Arrange — the shop's save holds in its interceptor, in the transaction it opened, while the ledger's is tried.
        var gate = new SaveTests.Gate();
        await using var host = HostWithLedger(out var ledger, vault => vault.AddInterceptor(gate));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        ledger().Entries.Add(new LedgerEntry { Id = 1, Text = "paid" });
        var first = host.Vault.SaveAsync();
        await gate.Entered.Task;

        // Act
        await Assert.That(() => ledger().SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        gate.Release.SetResult();
        await first;
        await Verify(new { Orders = await host.StoredAsync("Orders"), Entries = await host.StoredAsync("Entries") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task DisposeAsync_ScopeWithATransactionOpen_RollsItBack()
    {
        // Arrange — a request that begins a transaction, saves in it, and ends without ending it.
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)));
        var scope = host.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVaultTransactionManager>().BeginAsync();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();
        vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await vault.SaveAsync();

        // Act
        await scope.DisposeAsync();

        // Assert
        await Verify(new { log.Entries, Stored = await host.StoredAsync("Orders") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_FailingToJoinTheTransaction_RunsNoHooks()
    {
        // Arrange — the transaction starts on the registered client; the vault, on another, can't join it.
        var log = new HookLog();
        using var other = Mongo.CreateClient("other-client");
        await using var host = new VaultHost<ShopVault>(Mongo.Client, other.GetDatabase($"t{Guid.NewGuid():N}"),
            vault => vault.AddInterceptor(new RecordingInterceptor("shop", log)));
        await using var transaction = await host.Transactions.BeginAsync();
        _ = transaction.Session;
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Assert.That(log.Entries).IsEmpty();
    }
}
