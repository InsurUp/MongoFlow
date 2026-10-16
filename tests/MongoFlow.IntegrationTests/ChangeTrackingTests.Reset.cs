using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class ChangeTrackingTests
{
    [Test]
    public async Task SaveAsync_RetriedAfterAReset_WritesOnlyTheRetrysChanges()
    {
        // Arrange — the first attempt's save fails, another writer then changes the name it changed, and the retry
        // reads afresh.
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new FailingOnce()));
        var first = (await host.Vault.Customers.GetByKeyAsync(1))!;
        first.Name = "Ada Lovelace";
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
        await host.Database.GetCollection<Customer>("Customers")
            .UpdateOneAsync(x => x.Id == 1, Builders<Customer>.Update.Set(x => x.Name, "Ada King"));
        host.Vault.Reset();
        var second = (await host.Vault.Customers.GetByKeyAsync(1))!;
        second.Address.City = "London";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_ChangedAndQueuedThenReset_WritesNothing()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });
        host.Vault.Reset();

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task CommitAsync_SaveBeforeAReset_WritesWhatTheSaveWrote()
    {
        // Arrange — the reset forgets the customer, not what the save wrote in the transaction.
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        host.Vault.Reset();
        customer.Address.City = "London";
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task Reset_FromAnInterceptorOfTheVault_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = await SeededAsync(vault => vault.AddInterceptor(new ResettingTheVault()));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }
}
