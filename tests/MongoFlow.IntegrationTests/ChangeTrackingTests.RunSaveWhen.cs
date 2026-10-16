namespace MongoFlow.IntegrationTests;

public partial class ChangeTrackingTests
{
    [Test]
    public async Task SaveAsync_NothingToWriteAndATrackedDocumentRunsTheSave_RunsTheInterceptorsAndCommitsWhatTheyWrite()
    {
        // Arrange — the event isn't stored, so the invoice hasn't changed.
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(tracked)
            .AddInterceptor(new OutboxWriter()));
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, tracked.Seen, Outbox = await host.StoredAsync("Outbox") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_NothingToWriteAndNoTrackedDocumentRunsTheSave_RunsNoInterceptor()
    {
        // Arrange
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(tracked));
        await host.Vault.Invoices.GetByKeyAsync(1);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, tracked.Seen }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_NothingToWriteAndSavedAsyncThrows_RollsBackWhatTheInterceptorsWrote()
    {
        // Arrange — the failing interceptor's SavedAsync runs after the outbox has written.
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(new FailingOnce())
            .AddInterceptor(tracked)
            .AddInterceptor(new OutboxWriter()));
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { tracked.Seen, Outbox = await host.StoredAsync("Outbox") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_NothingToWriteAndAnInterceptorQueuesAWrite_WritesIt()
    {
        // Arrange
        await using var host = await InvoicedAsync(vault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(new OutboxQueuer()));
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Outbox = await host.StoredAsync("Outbox") });
    }

    [Test]
    public async Task SaveAsync_RunSaveWhenOnAVaultWithoutInterceptors_StartsNoSave()
    {
        // Arrange — nothing would read the invoice.
        await using var host = Mongo.Host<CrmVault>(vault => vault
            .UseChangeTracking()
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .Collection(x => x.Coupons, coupons => coupons.Key(x => x.Code!)));
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Status = "open" });
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");
        using var spans = new ActivitySink();

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(spans.Snapshot()).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_OnlyADocumentReadWithTheFeatureOffMatches_RunsNoInterceptor()
    {
        // Arrange
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault.AddFeature(new EventsFeature(tracked)));
        var invoice = (await host.Vault.Invoices.Without(EventsFeature.Key).GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(tracked.Seen).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_CollectionOptedOutOfTheFeature_RunsNoInterceptor()
    {
        // Arrange
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .AddFeature(new EventsFeature(tracked))
            .Collection(x => x.Invoices, invoices => invoices.Without(EventsFeature.Key)));
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.Events.Add("invoice-1-reminded");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(tracked.Seen).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Tracked_SaveWithWrites_HoldsTheTrackedDocumentsChangedOrNotInReadOrder()
    {
        // Arrange — invoice 1 changed and invoice 2 didn't, both with an event; invoice 3 was read without tracking.
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault.AddInterceptor(tracked).AddInterceptor(new OutboxWriter()));
        var second = (await host.Vault.Invoices.GetByKeyAsync(2))!;
        await host.Vault.Customers.GetByKeyAsync(1);
        var first = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        var untracked = (await host.Vault.Invoices.WithNoTracking().GetByKeyAsync(3))!;
        first.Status = "paid";
        first.Events.Add("invoice-1-paid");
        second.Events.Add("invoice-2-reminded");
        untracked.Events.Add("invoice-3-reminded");
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { tracked.Seen, Outbox = await host.StoredAsync("Outbox") });
    }

    [Test]
    public async Task Tracked_NothingRead_IsEmpty()
    {
        // Arrange
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault.AddInterceptor(tracked));
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Bob" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(tracked.Seen).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Tracked_DocumentAnEarlierSaveDeleted_IsLeftOut()
    {
        // Arrange — the delete commits with the transaction, so the vault still holds the deleted invoice.
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(tracked));
        await using var transaction = await host.Transactions.BeginAsync();
        var first = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        var second = (await host.Vault.Invoices.GetByKeyAsync(2))!;
        host.Vault.Invoices.Delete(first);
        await host.Vault.SaveAsync();
        second.Events.Add("invoice-2-reminded");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(tracked.Seen).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Tracked_OfAnInterceptorOnACollection_HoldsOnlyThatCollectionsDocuments()
    {
        // Arrange
        var customers = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault
            .Collection(x => x.Customers, collection => collection.AddInterceptor(customers)));
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        await host.Vault.Invoices.GetByKeyAsync(1);
        customer.Name = "Ada Lovelace";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(customers.Seen);
    }

    [Test]
    public async Task Tracked_OfAFeaturesInterceptor_LeavesOutDocumentsReadWithTheFeatureOff()
    {
        // Arrange — the invoice read with the feature off is checked first, and doesn't run the save; the other does.
        var tracked = new TrackedRecorder();
        await using var host = await InvoicedAsync(vault => vault.AddFeature(new EventsFeature(tracked)));
        var second = (await host.Vault.Invoices.Without(EventsFeature.Key).GetByKeyAsync(2))!;
        var first = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        second.Events.Add("invoice-2-reminded");
        first.Events.Add("invoice-1-reminded");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(tracked.Seen).DontIgnoreEmptyCollections();
    }

    private async Task<VaultHost<CrmVault>> InvoicedAsync(Action<IVaultBuilder<CrmVault>> configure)
    {
        var host = await SeededAsync(configure);
        await host.SeedAsync("Invoices",
            new Invoice { Id = 1, Status = "open" },
            new Invoice { Id = 2, Status = "open" },
            new Invoice { Id = 3, Status = "open" });

        return host;
    }
}
