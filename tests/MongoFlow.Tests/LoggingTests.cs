using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// What MongoFlow logs before it talks to the server, through the <see cref="ILoggerFactory"/> in DI:
/// <list type="number">
/// <item>building a vault's model, at <see cref="LogLevel.Debug"/>;</item>
/// <item>the query filters each read runs with, at <see cref="LogLevel.Trace"/>;</item>
/// <item>transactions begun and how they end, at <see cref="LogLevel.Debug"/>;</item>
/// <item>an index check that can't reach the server, at <see cref="LogLevel.Debug"/>, without failing anything;</item>
/// <item>every event in <see cref="MongoFlowLogEvents"/> has an ID of its own, in MongoFlow's range, clear of other
/// libraries'.</item>
/// </list>
/// Without an <see cref="ILoggerFactory"/> nothing is logged, as every other test shows.
/// </summary>
public class LoggingTests
{
    private readonly LogSink _sink = new();

    [Test]
    public async Task Build_WithAVault_LogsItsModel()
    {
        // Arrange
        await using var host = Host(vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));

        // Act
        _ = host.Vault;

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Model"));
    }

    [Test]
    public async Task Reads_WithQueryFilters_LogTheFilters()
    {
        // Arrange
        await using var host = Host(vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));

        // Act
        await host.Vault.Orders.QueryAsync();
        await host.Vault.Orders.FindAsync(x => x.Total > 10);
        await host.Vault.Audit.AggregateAsync();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Query"));
    }

    [Test]
    public async Task Transactions_BegunAndEnded_AreLogged()
    {
        // Arrange
        await using var host = Host();
        var committed = await host.Transactions.BeginAsync();
        await committed.CommitAsync();
        var rolledBack = await host.Transactions.BeginAsync();

        // Act
        await rolledBack.RollbackAsync();

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Transaction"));
    }

    [Test]
    public async Task IndexCheck_ServerUnreachable_LogsThatItCouldNotCheck()
    {
        // Arrange — soft delete gives orders a field to check; the offline client fails to list indexes.
        await using var host = Host(vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));
        _ = host.Vault;

        // Act
        await _sink.WaitForAsync(MongoFlowLogEvents.Indexes.IndexCheckCompleted);

        // Assert
        await Verify(_sink.Snapshot("MongoFlow.Indexes"));
    }

    [Test]
    public async Task MongoFlowLogEvents_EveryEvent_HasAnIdOfItsOwnInMongoFlowsRange()
    {
        // Act
        var events = typeof(MongoFlowLogEvents).GetNestedTypes()
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(int))
                .Select(field => new { Group = group.Name, Event = field.Name, Id = (int)field.GetRawConstantValue()! }))
            .ToList();

        // Assert
        await Assert.That(events.Select(item => item.Id)).HasDistinctItems();
        await Assert.That(events).All(item => item.Id is >= 27_000_000 and < 28_000_000);
        await Verify(events);
    }

    private VaultHost<ShopVault> Host(Action<IVaultBuilder<ShopVault>>? configure = null) =>
        new(configure, services => services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));
}
