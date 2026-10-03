using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// The document as it was before the save, on the writes that bring a tracked document up to date.
public partial class ChangeTrackingTests
{
    [Test]
    public async Task Original_TrackedChange_IsTheDocumentAsReadInEveryHook()
    {
        // Arrange — the original is serialized from the class, so it leaves out the stored field the class doesn't map.
        var originals = new OriginalRecorder("Saving", "Saved", "Committed");
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_FirstReadInTheCommittedHooks_IsTheDocumentAsRead()
    {
        // Arrange — the save gives back the snapshots it held, and forgets the customer it deleted, only once its hooks
        // have run. Registered last, the pool reuser's committed hook runs first, writing over what the pool holds.
        var originals = new OriginalRecorder("Committed");
        await using var host = await SeededAsync(vault => vault
            .AddInterceptor(originals)
            .AddInterceptor(new PoolReuser()));
        await host.SeedAsync("Customers", new Customer { Id = 2, Name = "Bob" });
        var ada = (await host.Vault.Customers.GetByKeyAsync(1))!;
        ada.Name = "Ada Lovelace";
        host.Vault.Customers.Delete((await host.Vault.Customers.GetByKeyAsync(2))!);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_QueuedReplaceAndDeletes_AreTheDocumentsAsRead()
    {
        // Arrange — the lead's delete becomes soft delete's update, which keeps the original.
        var originals = new OriginalRecorder("Saved");
        await using var host = await SeededAsync(vault => vault
            .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
            .AddInterceptor(originals));
        await host.SeedAsync("Customers", new Customer { Id = 2, Name = "Bob" });
        await host.SeedAsync("Leads", new Lead { Id = 3, Name = "cy" });
        var ada = (await host.Vault.Customers.GetByKeyAsync(1))!;
        ada.Name = "Ada Lovelace";
        host.Vault.Customers.Replace(ada);
        host.Vault.Customers.Delete((await host.Vault.Customers.GetByKeyAsync(2))!);
        host.Vault.Leads.Delete((await host.Vault.Leads.GetByKeyAsync(3))!);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_UntrackedWrites_IsNone()
    {
        // Arrange
        var originals = new OriginalRecorder("Saving");
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        var untracked = (await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1))!;
        host.Vault.Customers.Replace(untracked);
        host.Vault.Customers.UpdateByKey(1, Builders<Customer>.Update.Set(x => x.Note, "second"));
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_SecondSaveInATransaction_IsWhatTheFirstWrote()
    {
        // Arrange
        var originals = new OriginalRecorder("Saving", "Committed");
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        await using var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        customer.Name = "Countess";

        // Act
        await host.Vault.SaveAsync();
        await transaction.CommitAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_SaveThatFailed_IsTheDocumentAsReadInItsFailureHooks()
    {
        // Arrange — registered first, the recorder's failure hook runs after the save's changes are pending again.
        var originals = new OriginalRecorder("Failed");
        await using var host = await SeededAsync(vault => vault
            .AddInterceptor(originals)
            .AddInterceptor(new FailingOnce()));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_VaultDisposedBeforeItsTransactionRollsBack_IsStillTheDocumentAsRead()
    {
        // Arrange — the save wrote in the open transaction, so it holds the original, which the disposal leaves alone.
        var originals = new OriginalRecorder("Failed");
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        host.Vault.Dispose();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_ReadDuringTheSave_IsStillThereAfterIt()
    {
        // Arrange
        var originals = new OriginalRecorder("Saving");
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();

        // Act
        var original = originals.Operations[0].Original;

        // Assert
        await Assert.That(original!["Name"]).IsEqualTo((BsonValue)"Ada");
    }

    [Test]
    public async Task Original_FirstReadAfterTheSave_ThrowsInvalidOperationException()
    {
        // Arrange — the recorder keeps the operation without reading its original.
        var originals = new OriginalRecorder();
        await using var host = await SeededAsync(vault => vault.AddInterceptor(originals));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();

        // Act & Assert
        await Throws(() => originals.Operations[0].Original).IgnoreStackTrace();
    }

    [Test]
    public async Task Original_VaultDisposedWhileSaving_CantBeReadAnyMore()
    {
        // Arrange — registered last, the vault's disposal runs before the recorder's hook after the write. The save joins
        // an open transaction, which then rolls back.
        var originals = new OriginalRecorder("Saved", "Failed");
        await using var host = await SeededAsync(vault => vault
            .AddInterceptor(originals)
            .AddInterceptor(new DisposingTheVault()));
        var transaction = await host.Transactions.BeginAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();
        await transaction.RollbackAsync();

        // Assert
        await Verify(originals.Seen);
    }

    [Test]
    public async Task Original_UpdateReplacedThroughWithUpdate_IsKept()
    {
        // Arrange — the stamper runs before the recorder, and replaces the tracked update with one of its own.
        var originals = new OriginalRecorder("Saving");
        await using var host = await SeededAsync(vault => vault
            .AddInterceptor(new AuditStamper())
            .AddInterceptor(originals));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { originals.Seen, Stored = await host.StoredAsync("Customers") });
    }
}
