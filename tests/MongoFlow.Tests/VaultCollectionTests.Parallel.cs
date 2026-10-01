using TUnit.Assertions.Enums;

namespace MongoFlow.Tests;

public partial class VaultCollectionTests
{
    [Test]
    public async Task Add_FromParallelTasks_QueuesEveryDocumentOnce()
    {
        // Arrange
        const int Count = 10_000;
        await using var host = new VaultHost<ShopVault>();
        var vault = host.Vault;

        // Act
        Parallel.For(0, Count, id => vault.Orders.Add(new Order { Id = id }));

        // Assert
        await Assert.That(QueuedIds(vault)).IsEquivalentTo(Enumerable.Range(0, Count), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Add_SameDocumentFromParallelTasks_QueuesItOnce()
    {
        // Arrange
        var order = new Order { Id = 1 };
        await using var host = new VaultHost<ShopVault>();
        var vault = host.Vault;

        // Act
        Parallel.For(0, 10_000, _ => vault.Orders.Add(order));

        // Assert
        await Assert.That(QueuedIds(vault)).IsEquivalentTo([order.Id]);
    }

    private static int[] QueuedIds(ShopVault vault)
    {
        using var queued = vault.Runtime.Drain()!;

        return [.. queued.Span.ToArray().Select(operation => ((Order)operation.Document!).Id).Order()];
    }
}
