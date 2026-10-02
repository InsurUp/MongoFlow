using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class TelemetryTests
{
    [Test]
    public async Task SaveAsync_Succeeding_RecordsItsDurationAndOperations()
    {
        // Arrange
        await using var host = MeteredHost<ShopVault>();
        using var saves = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.SaveDuration);
        using var operations = Collect<long>(host.Services, MongoFlowTelemetry.Instruments.SaveOperations);
        await host.SeedAsync("Orders", new Order { Id = 2, Customer = "bob", Total = 20 });
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        host.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Total, 25));
        host.Vault.Orders.DeleteMany(x => x.Total > 100);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(saves.LastMeasurement!.Value).IsGreaterThan(0);
        await Verify(new { Saves = TagsOf(saves), Operations = Counted(operations) });
    }

    [Test]
    public async Task SaveAsync_AConcurrencyConflict_RecordsTheErrorTypeAndNoOperations()
    {
        // Arrange — the replace carries a token the stored ticket moved past.
        await using var host = MeteredHost<TicketVault>(vault => vault.UseConcurrencyToken((Ticket x) => x.Version));
        using var saves = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.SaveDuration);
        using var operations = Collect<long>(host.Services, MongoFlowTelemetry.Instruments.SaveOperations);
        await host.SeedAsync("Tickets", new Ticket { Id = 1, Version = 2 });
        host.Vault.Tickets.Replace(new Ticket { Id = 1, Version = 1 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(new { Saves = TagsOf(saves), Operations = Counted(operations) }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task CommitAsync_JoinedSaves_CountTheirOperationsOnlyOnCommit()
    {
        // Arrange
        await using var host = MeteredHost<ShopVault>();
        using var saves = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.SaveDuration);
        using var operations = Collect<long>(host.Services, MongoFlowTelemetry.Instruments.SaveOperations);
        using var transactions = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.TransactionDuration);
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        host.Vault.Orders.AddRange([new Order { Id = 2, Customer = "bob", Total = 20 }, new Order { Id = 3, Customer = "cy", Total = 30 }]);
        await host.Vault.SaveAsync();
        var beforeCommit = Counted(operations);

        // Act
        await transaction.CommitAsync();

        // Assert
        await Verify(new
        {
            Saves = TagsOf(saves),
            BeforeCommit = beforeCommit,
            Operations = Counted(operations),
            Transactions = TagsOf(transactions)
        }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task RollbackAsync_SavesInTheTransaction_RecordsRolledBackAndNoOperations()
    {
        // Arrange
        await using var host = MeteredHost<ShopVault>();
        using var operations = Collect<long>(host.Services, MongoFlowTelemetry.Instruments.SaveOperations);
        using var transactions = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.TransactionDuration);
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Verify(new { Operations = Counted(operations), Transactions = TagsOf(transactions) }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_FailingAfterWritingInATransaction_RecordsTheTransactionRolledBackOnce()
    {
        // Arrange — the transaction is rolled back when the save fails, and disposed after.
        await using var host = MeteredHost<ShopVault>(vault => vault.AddInterceptor(new FailingAfterTheWrite()));
        using var saves = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.SaveDuration);
        using var transactions = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.TransactionDuration);
        var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
        await transaction.DisposeAsync();

        // Assert
        await Verify(new { Saves = TagsOf(saves), Transactions = TagsOf(transactions) });
    }

    [Test]
    [NotInParallel("failCommand")]
    public async Task CommitAsync_ServerRejectsIt_RecordsCommitFailedAndNoOperations()
    {
        // Arrange — the server has one failCommand failpoint, so tests setting it don't run in parallel.
        var name = $"commit-{Guid.NewGuid():N}";
        using var client = Mongo.CreateClient(name);
        await using var host = new VaultHost<ShopVault>(client, client.GetDatabase($"t{Guid.NewGuid():N}"),
            services: services => services.AddMetrics());
        using var operations = Collect<long>(host.Services, MongoFlowTelemetry.Instruments.SaveOperations);
        using var transactions = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.TransactionDuration);
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        await Mongo.FailNextAsync("commitTransaction", name);

        // Act
        await Assert.That(() => transaction.CommitAsync()).ThrowsExactly<MongoCommandException>();

        // Assert
        await Verify(new { Operations = Counted(operations), Transactions = TagsOf(transactions) }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SaveAsync_InItsOwnTransaction_RecordsNoTransaction()
    {
        // Arrange
        await using var host = MeteredHost<ShopVault>();
        using var transactions = Collect<double>(host.Services, MongoFlowTelemetry.Instruments.TransactionDuration);
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(transactions.GetMeasurementSnapshot()).IsEmpty();
    }

    private VaultHost<TVault> MeteredHost<TVault>(Action<IVaultBuilder<TVault>>? configure = null) where TVault : MongoVault =>
        Mongo.Host(configure, services => services.AddMetrics());

    /// <summary>What <paramref name="instrument"/> measures on the host's meter, which no other test's host shares.</summary>
    private static MetricCollector<T> Collect<T>(IServiceProvider services,
        string instrument) where T : struct =>
        new(services.GetRequiredService<IMeterFactory>(), MongoFlowTelemetry.MeterName, instrument);

    // Durations change from run to run, so a snapshot shows what they're tagged with.
    private static List<IReadOnlyDictionary<string, object?>> TagsOf(MetricCollector<double> durations) =>
        [.. durations.GetMeasurementSnapshot().Select(measurement => measurement.Tags)];

    private static List<object> Counted(MetricCollector<long> counter) =>
        [.. counter.GetMeasurementSnapshot().Select(measurement => new { measurement.Value, measurement.Tags })];
}
