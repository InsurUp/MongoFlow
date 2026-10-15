using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Reads by filter in one call return what <c>FindAsync(filter)</c> followed by the driver's operator returns:
/// <list type="number">
/// <item>with an expression or a filter built with the driver, they match it and the query filters, on a view with what
/// the view switches off;</item>
/// <item><c>FirstAsync</c> and <c>SingleAsync</c> fail when nothing the query filters show matches, and the
/// <c>Single</c> reads when two documents do;</item>
/// <item>inside a transaction they see its writes before the commit;</item>
/// <item>the documents they return are tracked when the vault or the view tracks changes.</item>
/// </list>
/// </summary>
public class VaultCollectionReadExtensionsTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Reads_ExpressionFilter_MatchItAndTheQueryFilters()
    {
        // Arrange — the query filters hide the order with Id 2.
        await using var host = await SeededAsync();
        var orders = host.Vault.Orders;

        // Act
        var reads = new
        {
            ToList = (await orders.ToListAsync(x => x.Total > 5)).Select(x => x.Id).Order(),
            FirstOrDefault = await orders.FirstOrDefaultAsync(x => x.Total > 25),
            First = await orders.FirstAsync(x => x.Id == 1),
            SingleOrDefault = await orders.SingleOrDefaultAsync(x => x.Total > 15),
            Single = await orders.SingleAsync(x => x.Customer == "ada"),
            Any = await orders.AnyAsync(x => x.Id == 2),
            Count = await orders.CountAsync(x => x.Total > 5),
            CountWithTheFeatureOff = await orders.Without(HiddenFeature.Key).CountAsync(x => x.Total > 5)
        };

        // Assert
        await Verify(reads);
    }

    [Test]
    public async Task Reads_DriverFilter_MatchItAndTheQueryFilters()
    {
        // Arrange — the query filters hide the order with Id 2.
        await using var host = await SeededAsync();
        var orders = host.Vault.Orders;
        var filter = Builders<Order>.Filter;

        // Act
        var reads = new
        {
            ToList = (await orders.ToListAsync(filter.Gt(x => x.Total, 5))).Select(x => x.Id).Order(),
            FirstOrDefault = await orders.FirstOrDefaultAsync(filter.Gt(x => x.Total, 25)),
            First = await orders.FirstAsync(filter.Eq(x => x.Id, 1)),
            SingleOrDefault = await orders.SingleOrDefaultAsync(filter.Gt(x => x.Total, 15)),
            Single = await orders.SingleAsync(filter.Eq(x => x.Customer, "ada")),
            Any = await orders.AnyAsync(filter.Eq(x => x.Id, 2)),
            Count = await orders.CountAsync(filter.Gt(x => x.Total, 5)),
            CountWithTheFeatureOff = await orders.Without(HiddenFeature.Key).CountAsync(filter.Gt(x => x.Total, 5))
        };

        // Assert
        await Verify(reads);
    }

    [Test]
    public async Task FirstAsyncAndSingleAsync_OnlyAHiddenDocumentMatches_ThrowInvalidOperationException()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act & Assert
        await Assert.That(Task () => host.Vault.Orders.FirstAsync(x => x.Id == 2)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(Task () => host.Vault.Orders.SingleAsync(x => x.Id == 2)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SingleAsyncAndSingleOrDefaultAsync_TwoMatches_ThrowInvalidOperationException()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act & Assert
        await Assert.That(Task () => host.Vault.Orders.SingleAsync(x => x.Total > 5)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(Task () => host.Vault.Orders.SingleOrDefaultAsync(x => x.Total > 5)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Reads_InATransaction_SeeItsUncommittedWrites()
    {
        // Arrange
        await using var host = await SeededAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Orders.Add(new Order { Id = 4, Customer = "dee", Total = 40 });
        await host.Vault.SaveAsync();
        await using var outside = host.CreateScope();

        // Act
        var reads = new
        {
            ToList = (await host.Vault.Orders.ToListAsync(x => x.Id == 4)).Count,
            Any = await host.Vault.Orders.AnyAsync(x => x.Id == 4),
            Count = await host.Vault.Orders.CountAsync(x => x.Total > 5),
            SeenOutside = await outside.ServiceProvider.GetRequiredService<ShopVault>().Orders.AnyAsync(x => x.Id == 4)
        };

        // Assert
        await Verify(reads);
    }

    [Test]
    public async Task Reads_TrackingVault_TrackWhatTheyReturnUnlessTheViewDoesnt()
    {
        // Arrange — the order read through a view without tracking is changed too, and isn't written.
        await using var host = await SeededAsync(vault => vault.UseChangeTracking());
        var first = (await host.Vault.Orders.FirstOrDefaultAsync(x => x.Id == 1))!;
        var listed = (await host.Vault.Orders.ToListAsync(x => x.Id == 3)).Single();
        var untracked = await host.Vault.Orders.WithNoTracking().SingleAsync(x => x.Customer == "ada");
        first.Total = 11;
        listed.Total = 31;
        untracked.Customer = "renamed";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Orders") });
    }

    private async Task<VaultHost<ShopVault>> SeededAsync(Action<IVaultBuilder<ShopVault>>? configure = null)
    {
        var host = Mongo.Host<ShopVault>(vault =>
        {
            vault.AddFeature(new HiddenFeature());
            configure?.Invoke(vault);
        });
        await host.SeedAsync("Orders",
            new Order { Id = 1, Customer = "ada", Total = 10 },
            new Order { Id = 2, Customer = HiddenFeature.Customer, Total = 20 },
            new Order { Id = 3, Customer = "bob", Total = 30 });

        return host;
    }
}
