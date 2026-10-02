using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class ChangeTrackingTests
{
    [Test]
    public async Task SaveAsync_FailingAfterItsWrite_LeavesTheChangesPending()
    {
        // Arrange
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new FailingOnce()));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_SoftDeleteThatFailed_LeavesTheDocumentAsItWas()
    {
        // Arrange — the delete's save fails, so the document isn't deleted, nor marked as if it were.
        await using var host = Host(vault => vault
            .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
            .AddInterceptor(new FailingOnce()));
        await host.SeedAsync("Leads", new Lead { Id = 1, Name = "ada" });
        var lead = (await host.Vault.Leads.GetByKeyAsync(1))!;
        host.Vault.Leads.Delete(lead);
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, lead.IsDeleted, Stored = await host.StoredAsync("Leads") });
    }

    [Test]
    public async Task RollbackAsync_SaveThatJoinedIt_LeavesTheChangesPending()
    {
        // Arrange
        await using var host = await SeededAsync();
        var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task RollbackAsync_SaveThatDeletedATrackedDocument_KeepsTrackingIt()
    {
        // Arrange
        await using var host = await SeededAsync();
        var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        host.Vault.Customers.Delete(customer);
        await host.Vault.SaveAsync();
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        customer.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task CommitAsync_TwoSavesJoinedIt_WriteEachChangeOnce()
    {
        // Arrange
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        customer.Address.City = "London";
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentWithAToken_ChecksAndIncrementsIt()
    {
        // Arrange — the second change is compared with the token the first save incremented.
        await using var host = Host(vault => vault.UseConcurrencyToken((IVersioned x) => x.Version));
        await host.SeedAsync("Accounts", new Account { Id = 1, Balance = 100 });
        var account = (await host.Vault.Accounts.GetByKeyAsync(1))!;
        account.Balance = 90;
        await host.Vault.SaveAsync();
        account.Balance = 80;

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, account, Stored = await host.StoredAsync("Accounts") });
    }

    [Test]
    public async Task SaveAsync_TrackedChangeAndQueuedWrites_WritesTheChangeFirst()
    {
        // Arrange — the changed account is then deleted by key, so its update goes first and finds the token it was read
        // with; the other account is replaced untracked.
        await using var host = Host(vault => vault.UseConcurrencyToken((IVersioned x) => x.Version));
        await host.SeedAsync("Accounts", new Account { Id = 1, Balance = 100 }, new Account { Id = 2, Balance = 200 });
        var account = (await host.Vault.Accounts.GetByKeyAsync(1))!;
        account.Balance = 90;
        host.Vault.Accounts.DeleteByKey(1);
        host.Vault.Accounts.Replace(new Account { Id = 2, Balance = 250 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Accounts") });
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentChangedSinceRead_ThrowsConcurrencyExceptionAndKeepsItsChanges()
    {
        // Arrange — another request moves the stored token on.
        await using var host = Host(vault => vault.UseConcurrencyToken((IVersioned x) => x.Version));
        await host.SeedAsync("Accounts", new Account { Id = 1, Balance = 100 });
        var account = (await host.Vault.Accounts.GetByKeyAsync(1))!;
        account.Balance = 90;
        await host.Database.GetCollection<Account>("Accounts")
            .UpdateOneAsync(x => x.Id == 1, Builders<Account>.Update.Set(x => x.Version, 1));
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Act & Assert — the change is still pending, so the next save meets the same conflict.
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();
    }

    [Test]
    public async Task Dispose_DuringASave_LetsItFinishAndForgetsTheTrackedDocuments()
    {
        // Arrange
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new DisposingTheVault()));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        customer.Address.City = "London";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task Dispose_WithASaveInAnOpenTransaction_LeavesTheRollbackToUndoIt()
    {
        // Arrange — the vault is disposed before the transaction it saved in ends.
        await using var host = await SeededAsync();
        var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        host.Vault.Dispose();

        // Act
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();

        // Assert
        await Verify(await host.StoredAsync("Customers"));
    }

    [Test]
    public async Task Dispose_WithADeleteInAnOpenTransaction_LeavesTheCommitToWriteIt()
    {
        // Arrange — the vault is disposed before the transaction it saved in ends.
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        host.Vault.Customers.Delete(customer);
        await host.Vault.SaveAsync();
        host.Vault.Dispose();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(await host.StoredAsync("Customers")).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Dispose_ByTheScope_GivesBackWhatTheVaultHolds()
    {
        // Arrange — a scope of its own, with a tracked document and a write never saved.
        await using var host = await SeededAsync();
        var scope = host.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<CrmVault>();
        await vault.Customers.GetByKeyAsync(1);
        vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });

        // Act & Assert
        await Assert.That(() => scope.DisposeAsync().AsTask()).ThrowsNothing();
    }
}
