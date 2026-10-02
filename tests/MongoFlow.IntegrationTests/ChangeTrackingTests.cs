using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Change tracking (<see cref="IVaultBuilder{TVault}.UseChangeTracking"/>): a save writes what changed in the documents
/// reads returned, with no write queued:
/// <list type="number">
/// <item>only what changed is written, by the key it was read with, so fields stored but not mapped survive; nothing
/// changed, nothing is written;</item>
/// <item>embedded fields are set by path, arrays whole, removed fields unset, and an element name no path can hold makes
/// it a replace;</item>
/// <item>once written, a change isn't written again; a key can't change;</item>
/// <item>a queued replace or delete of a tracked document takes the place of its changes, and a delete stops tracking
/// it; a queued update doesn't, and goes after them;</item>
/// <item>which reads track; see <c>ChangeTrackingTests.Reads.cs</c>. Transactions, failures, the concurrency token and
/// the vault's disposal; see <c>ChangeTrackingTests.Transactions.cs</c>.</item>
/// </list>
/// </summary>
public partial class ChangeTrackingTests
{
    private readonly WriteRecorder _recorder = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_TrackedDocumentChanged_WritesOnlyWhatChanged()
    {
        // Arrange — the stored customer has a field the class doesn't map.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        customer.Address.City = "London";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_NothingChanged_WritesNothing()
    {
        // Arrange
        await using var host = await SeededAsync();
        await host.Vault.Customers.GetByKeyAsync(1);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_FieldLeftOutAndListChanged_UnsetsOneAndSetsTheOtherWhole()
    {
        // Arrange — a null note isn't serialized, so it's removed.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Note = null;
        customer.Tags.Add("vip");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_SavedAgainAndChangedAgain_WritesEachChangeOnce()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        await host.Vault.SaveAsync();
        await host.Vault.SaveAsync();
        customer.Address.Zip = "NW1";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_ElementNameNoPathCanHold_ReplacesTheDocument()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Prices", new Price { Id = 1, Amount = 10 });
        var price = (await host.Vault.Prices.GetByKeyAsync(1))!;
        price.Amount = 12;

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Prices") });
    }

    [Test]
    public async Task SaveAsync_TrackedKeyChanged_ThrowsAndWritesNothing()
    {
        // Arrange — an insert is queued too, and goes with the failed save.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Id = 2;
        host.Vault.Customers.Add(new Customer { Id = 3, Name = "Cy" });

        // Act
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();

        // Assert
        await Assert.That((await host.StoredAsync("Customers")).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SaveAsync_TrackedKeyChangedWithNothingQueued_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Id = 2;

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentReplaced_WritesTheReplaceInsteadOfItsChanges()
    {
        // Arrange — the change after the replace is compared with what the replace wrote.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        host.Vault.Customers.Replace(customer);
        await host.Vault.SaveAsync();
        customer.Address.City = "London";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentDeleted_WritesTheDeleteAndStopsTrackingIt()
    {
        // Arrange — replaced and deleted in one save, it's deleted.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Name = "Ada Lovelace";
        host.Vault.Customers.Replace(customer);
        host.Vault.Customers.Delete(customer);
        await host.Vault.SaveAsync();
        customer.Address.City = "London";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, _recorder.Writes, Stored = await host.StoredAsync("Customers") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentSoftDeleted_StopsTrackingIt()
    {
        // Arrange
        await using var host = Host(vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));
        await host.SeedAsync("Leads", new Lead { Id = 1, Name = "ada" });
        var lead = (await host.Vault.Leads.GetByKeyAsync(1))!;
        host.Vault.Leads.Delete(lead);
        await host.Vault.SaveAsync();
        lead.Name = "Ada Lovelace";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Leads") });
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentUpdatedToo_WritesItsChangesThenTheUpdate()
    {
        // Arrange
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Address.City = "London";
        host.Vault.Customers.Update(customer, Builders<Customer>.Update.Set(x => x.Note, "met"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { _recorder.Writes, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task SaveAsync_ReadWithSoftDeleteOff_UpdatesWithItOff()
    {
        // Arrange — the vault doesn't track; the view does, and keeps doing so with a feature off.
        await using var host = Host(vault => vault.UseChangeTracking(false).UseSoftDelete((ISoftDeletable x) => x.IsDeleted));
        await host.SeedAsync("Leads", new Lead { Id = 1, Name = "ada", IsDeleted = true });
        var lead = (await host.Vault.Leads.WithTracking().Without(SoftDeleteFeature.Key).GetByKeyAsync(1))!;
        lead.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Leads"));
    }

    private VaultHost<CrmVault> Host(Action<IVaultBuilder<CrmVault>>? configure = null) =>
        Mongo.Host<CrmVault>(vault =>
        {
            vault.UseChangeTracking().AddInterceptor(_recorder);
            vault.Collection(x => x.Coupons, coupons => coupons.Key(x => x.Code!));
            configure?.Invoke(vault);
        });

    private async Task<VaultHost<CrmVault>> SeededAsync(Action<IVaultBuilder<CrmVault>>? configure = null)
    {
        var host = Host(configure);
        await host.SeedAsync("Customers", new BsonDocument
        {
            { "_id", 1 },
            { "Name", "Ada" },
            { "Address", new BsonDocument { { "City", "Izmir" }, { "Zip", "35000" } } },
            { "Note", "first" },
            { "Tags", new BsonArray { "early" } },
            { "Legacy", "kept" }
        });

        return host;
    }
}
