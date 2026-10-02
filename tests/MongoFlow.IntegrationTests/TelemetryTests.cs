using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// What MongoFlow traces and measures, under <see cref="MongoFlowTelemetry.ActivitySourceName"/> and
/// <see cref="MongoFlowTelemetry.MeterName"/>:
/// <list type="number">
/// <item>a save with writes queued is a span, tagged with what it wrote and how it ended, with the driver's spans for what
/// it sent nested under it;</item>
/// <item>the driver's span for a begun transaction starts under the code that began it, not under the first save;</item>
/// <item>each vault migrated, and each migration applied or reverted, is a span; see <c>TelemetryTests.Migrations.cs</c>;</item>
/// <item>saves' durations, the writes they committed and begun transactions' durations are measured; see
/// <c>TelemetryTests.Metrics.cs</c>.</item>
/// </list>
/// </summary>
public partial class TelemetryTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_Succeeding_TracesTheSave()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Total, 25));
        host.Vault.Orders.DeleteMany(x => x.Total > 100);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    public async Task SaveAsync_InAnOpenTransaction_TracesItAsJoined()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    public async Task SaveAsync_AWriteFails_TracesTheFailure()
    {
        // Arrange — the insert repeats a stored key.
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    public async Task SaveAsync_NothingQueued_TracesNothing()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(spans.Snapshot()).IsEmpty();
    }

    [Test]
    public async Task SaveAsync_InItsOwnTransaction_NestsTheDriversSpans()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(spans.Snapshot(DriverSpans));
    }

    [Test]
    public async Task BeginAsync_SavesInTheTransaction_StartsTheDriversTransactionSpanUnderTheCaller()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(spans.Snapshot(DriverSpans));
    }

    [Test]
    public async Task MigrateAsyncAndSaveAsync_SampledOnlyToPropagate_TagNothing()
    {
        // Arrange — the migration saves through the vault, and the save after it repeats the migration's key.
        using var spans = new ActivitySink(ActivitySamplingResult.PropagationData);
        await using var host = Mongo.Host<ShopVault>(vault => vault.Migrations(m => m.Add<AddOrder>()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAsync<ShopVault>();
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Verify(spans.Snapshot());
    }

    // The driver's operation spans for what a save sends, without the command span each one starts. The bulk-write
    // support check runs once per client, so whichever test saves first would show it.
    private static readonly string[] DriverSpans = ["transaction", "bulkWrite admin", "commitTransaction admin"];
}
