using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// What MongoFlow logs around saves, through the <see cref="ILoggerFactory"/> in DI:
/// <list type="number">
/// <item>a save starting, each operation it writes (at <see cref="LogLevel.Trace"/>) and how it ends;</item>
/// <item>what fails a save: a write the server rejects, a concurrency conflict, a write to another tenant, a commit;</item>
/// <item>an interceptor's failure hook throwing, which is swallowed and so logged as an error;</item>
/// <item>the key and query filters a lookup by key runs with, at <see cref="LogLevel.Trace"/>;</item>
/// <item>collections missing the indexes their key or features rely on; see <c>LoggingTests.Indexes.cs</c>.</item>
/// </list>
/// </summary>
public partial class LoggingTests
{
    private readonly LogSink _sink = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_Succeeding_LogsItsStartWritesAndEnd()
    {
        // Arrange
        await using var host = Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Total, 25));
        host.Vault.Orders.DeleteMany(x => x.Total > 100);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Save").Where(entry => entry.EventId != MongoFlowLogEvents.Save.BulkWritesSupported));
    }

    [Test]
    public async Task SaveAsync_InAnOpenTransaction_LogsTheTransaction()
    {
        // Arrange
        await using var host = Host<ShopVault>();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.CommitAsync();

        // Assert — the index check logs in the background, and the bulk-write check once per client, so neither is shown.
        await Verify(_sink.Snapshot().Where(entry =>
            entry.Level >= LogLevel.Debug && entry.Category is "MongoFlow.Save" or "MongoFlow.Transaction" && entry.EventId != MongoFlowLogEvents.Save.BulkWritesSupported));
    }

    [Test]
    public async Task SaveAsync_AWriteFails_LogsTheFailure()
    {
        // Arrange
        await using var host = Host<ShopVault>();
        await host.SeedAsync("Orders", new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ClientBulkWriteException>();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Save").Where(entry => entry.EventId == MongoFlowLogEvents.Save.SaveFailed));
    }

    [Test]
    public async Task SaveAsync_AFailureHookThrows_LogsItAsAnError()
    {
        // Arrange
        await using var host = Host<ShopVault>(vault => vault
            .AddInterceptor(new ThrowingOnFailure())
            .AddInterceptor(new FailingAfterTheWrite()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Save").Where(entry => entry.Level == LogLevel.Error));
    }

    [Test]
    public async Task SaveAsync_ConcurrencyConflict_LogsIt()
    {
        // Arrange
        await using var host = Host<VersionedVault>(vault => vault.UseConcurrencyToken((Ticket x) => x.Version));
        await host.SeedAsync("Tickets", new Ticket { Id = 1, Version = 2 });
        host.Vault.Tickets.Replace(new Ticket { Id = 1, Version = 1 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Save").Where(entry => entry.EventId == MongoFlowLogEvents.Save.ConcurrencyConflict));
    }

    [Test]
    public async Task SaveAsync_WriteForAnotherTenant_LogsIt()
    {
        // Arrange
        await using var host = Host<TenantVault>(vault => vault.UseMultiTenancy((Bill x) => x.TenantId, _ => "t-1"));
        host.Vault.Bills.Add(new Bill { Id = 1, TenantId = "t-2" });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Save").Where(entry => entry.EventId == MongoFlowLogEvents.Save.TenantRejected));
    }

    [Test]
    public async Task GetByKeyAsync_WithAndWithoutQueryFilters_LogsTheKeyAndFilters()
    {
        // Arrange
        await using var host = Host<ShopVault>(vault => vault.AddFeature(new HiddenFeature()));

        // Act
        await host.Vault.Orders.GetByKeyAsync(1);
        await host.Vault.Orders.Without(HiddenFeature.Key).GetByKeyAsync(2);

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Query"));
    }

    [Test]
    [NotInParallel("failCommand")]
    public async Task CommitAsync_ServerRejectsIt_LogsTheFailure()
    {
        // Arrange — the server has one failCommand failpoint, so tests setting it don't run in parallel.
        var name = $"commit-{Guid.NewGuid():N}";
        using var client = Mongo.CreateClient(name);
        await using var host = new VaultHost<ShopVault>(client, client.GetDatabase($"t{Guid.NewGuid():N}"), services: Logging);
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        await Mongo.FailNextAsync("commitTransaction", name);

        // Act
        await Assert.That(() => transaction.CommitAsync()).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Transaction").Where(entry => entry.EventId == MongoFlowLogEvents.Transaction.CommitFailed));
    }

    private VaultHost<TVault> Host<TVault>(Action<IVaultBuilder<TVault>>? configure = null) where TVault : MongoVault =>
        Mongo.Host(configure, Logging);

    private void Logging(IServiceCollection services) =>
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink));
}
