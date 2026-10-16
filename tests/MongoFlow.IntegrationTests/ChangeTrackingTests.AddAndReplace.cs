using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class ChangeTrackingTests
{
    [Test]
    public async Task SaveAsync_DocumentAddedThenChanged_WritesTheChange()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        await host.Vault.SaveAsync();
        customer.Address.City = "Ankara";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_DocumentReplacedUntrackedThenChanged_WritesTheChange()
    {
        // Arrange — read without tracking, the customer is tracked once the replace writes it.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        host.Vault.Customers.Replace(customer);
        await host.Vault.SaveAsync();
        customer.Note = "met";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_DocumentAddedWithATokenThenChanged_ChecksAndIncrementsIt()
    {
        // Arrange
        await using var host = Host(vault => vault.UseConcurrencyToken((IVersioned x) => x.Version));
        var account = new Account { Id = 1, Balance = 100 };
        host.Vault.Accounts.Add(account);
        await host.Vault.SaveAsync();
        account.Balance = 90;

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, account, Stored = await host.StoredAsync("Accounts") });
    }

    [Test]
    public async Task SaveAsync_DocumentAddedThroughATrackingViewOfAVaultThatDoesnt_WritesItsChange()
    {
        // Arrange
        await using var host = await SeededAsync(vault => vault.UseChangeTracking(false));
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.WithTracking().Add(customer);
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_DocumentAddedWithoutTracking_LeavesItUntracked()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.WithNoTracking().Add(customer);
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes });
    }

    [Test]
    public async Task SaveAsync_DocumentAddedWithoutAKey_LeavesItUntracked()
    {
        // Arrange — coupons are keyed by a code this one lacks.
        await using var host = Host();
        var coupon = new Coupon { Id = ObjectId.Parse("665f1c2e9b1e8a3d4c5b6a71"), Percent = 10 };
        host.Vault.Coupons.Add(coupon);
        await host.Vault.SaveAsync();
        coupon.Percent = 20;

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Coupons") });
    }

    [Test]
    public async Task SaveAsync_DocumentAddedAndUpdatedInOneSave_LeavesItUntracked()
    {
        // Arrange — the update stores what the document doesn't hold, so it can't be compared against.
        await using var host = await SeededAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        host.Vault.Customers.Update(customer, Builders<Customer>.Update.Set(x => x.Note, "met"));
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_ReplaceThatMatchedNothing_LeavesTheDocumentUntracked()
    {
        // Arrange — no customer 2 is stored, so the replace writes nothing.
        await using var host = await SeededAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Replace(customer);
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes });
    }

    [Test]
    public async Task SaveAsync_SaveThatAddedADocumentFailed_LeavesItUntracked()
    {
        // Arrange — the failed save's insert went with it.
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new FailingOnce()));
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task CommitAsync_DocumentAddedThenChangedInIt_WritesBoth()
    {
        // Arrange
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task RollbackAsync_SaveThatAddedADocument_LeavesItUntracked()
    {
        // Arrange
        await using var host = await SeededAsync();
        var transaction = await host.Transactions.BeginAsync();
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        await host.Vault.SaveAsync();
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task Dispose_DuringASaveThatAddsADocument_LeavesItUntracked()
    {
        // Arrange — a vault disposed starts again with nothing tracked.
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new DisposingTheVault()));
        var customer = new Customer { Id = 2, Name = "Bob" };
        host.Vault.Customers.Add(customer);
        await host.Vault.SaveAsync();
        customer.Name = "Bobby";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task Dispose_WithAnInsertInAnOpenTransaction_LeavesTheRollbackToUndoIt()
    {
        // Arrange — the vault is disposed before the transaction it saved in ends.
        await using var host = await SeededAsync();
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });
        await host.Vault.SaveAsync();
        host.Vault.Dispose();

        // Act
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();

        // Assert
        await Verify(await host.StoredAsync("Customers"));
    }
}
