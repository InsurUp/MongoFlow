using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

public partial class IndexTests
{
    [Test]
    public async Task RecreateIndexAsync_InAMigration_RecreatesWhatCantChangeInPlace()
    {
        // Arrange — the number indexed by hand, not unique, as the vault declared it before.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number), index => index.Unique()),
            migrations => migrations.Add<MakeNumbersUnique>());
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Number)));

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Indexes = await IndexesAsync(host.Database), Logs = Logged() });
    }

    [Test]
    public async Task RecreateIndexAsync_DuplicatesStored_FailsTheMigration()
    {
        // Arrange — two orders with one number, which the index declared unique refuses.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Number), index => index.Unique()),
            migrations => migrations.Add<MakeNumbersUnique>());
        await host.SeedAsync("Orders", new Order { Id = 1, Number = "A-1" }, new Order { Id = 2, Number = "A-1" });

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new { Failed = exception!.InnerException!.GetType().Name, Indexes = await NamesAsync(host.Database) });
    }

    [Test]
    public async Task ApplyIndexesAsync_InAMigration_AppliesThemBeforeTheMigrationsAfterIt()
    {
        // Arrange — the second migration saves two orders with one number.
        await using var host = Host(orders => orders
                .Index(keys => keys.Ascending(x => x.Number), index => index.Unique())
                .Index(keys => keys.Ascending(x => x.Customer)),
            migrations => migrations.Add<IndexOrders>().Add<DuplicateNumbers>());

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new
        {
            Failed = exception!.InnerException!.GetType().Name,
            Indexes = await NamesAsync(host.Database),
            Logs = Logged()
        });
    }

    [Test]
    public async Task ApplyIndexesAsync_InTheMigrationsTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)),
            migrations => migrations.Add<IndexOrdersInATransaction>());

        // Act
        var exception = await Assert.That(() => MigrateAsync(host)).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(Scrubbed(exception!.InnerException!.Message));
    }

    [Test]
    public async Task RecreateIndexAsync_NameNotDeclared_DropsItAndWarns()
    {
        // Arrange — an index the vault no longer declares.
        await using var host = Host(orders => orders.Index(keys => keys.Ascending(x => x.Customer)),
            migrations => migrations.Add<RecreateRetired>());
        await host.Database.GetCollection<Order>("Orders").Indexes.CreateOneAsync(new CreateIndexModel<Order>(
            Builders<Order>.IndexKeys.Ascending(x => x.Notes), new CreateIndexOptions { Name = "Retired_1" }));

        // Act
        await MigrateAsync(host);

        // Assert
        await Verify(new { Indexes = await NamesAsync(host.Database), Logs = Logged() });
    }
}
