using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What saving changes made in memory costs: each row reads the orders, adds 1 to each total, and saves. The driver reads
/// them and writes each new total with <c>$set</c> in a client bulk write; MongoFlow reads them untracked and queues the
/// same <c>$set</c> by key, or reads them tracked, which keeps each order's BSON, and saves what changed, which serializes
/// each order again to compare. The last row reads tracked and changes nothing, so its save only compares. Every save
/// sets each total one higher, so saves repeat on the orders seeded once.
/// </summary>
[MemoryDiagnoser]
public class TrackedSaveBenchmarks : IDisposable
{
    private BenchmarkDatabase _database = null!;
    private ServiceProvider _plain = null!;
    private ServiceProvider _tracked = null!;

    [DocumentCountParams]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = new BenchmarkDatabase();
        _database.Seed(Count);
        _plain = Vaults.Plain(_database.Database);
        _tracked = Vaults.Build(_database.Database, vault => vault.UseChangeTracking());

        // The first save asks the server whether it supports client bulk writes; that isn't measured.
        await MongoFlow();
    }

    [Benchmark(Baseline = true, Description = "Driver: find, then client bulk write")]
    public async Task<ClientBulkWriteResult> Driver()
    {
        var orders = await _database.Orders.Find(FilterDefinition<Order>.Empty).ToListAsync();
        var models = new BulkWriteModel[orders.Count];
        for (var i = 0; i < orders.Count; i++)
        {
            models[i] = new BulkWriteUpdateOneModel<Order>(_database.Orders.CollectionNamespace,
                Builders<Order>.Filter.Eq(x => x.Id, orders[i].Id),
                Builders<Order>.Update.Set(x => x.Total, orders[i].Total + 1));
        }

        return await ClientBulkWrites.WriteAsync(_database.Client, models);
    }

    [Benchmark(Description = "MongoFlow: read, then UpdateByKey")]
    public async Task<SaveResult> MongoFlow()
    {
        await using var scope = _plain.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        foreach (var order in await (await vault.Orders.FindAsync(_ => true)).ToListAsync())
        {
            vault.Orders.UpdateByKey(order.Id, Builders<Order>.Update.Set(x => x.Total, order.Total + 1));
        }

        return await vault.SaveAsync();
    }

    [Benchmark(Description = "MongoFlow: tracked read, then save")]
    public Task<SaveResult> MongoFlowTracked() => TrackedAsync(order => order.Total++);

    [Benchmark(Description = "MongoFlow: tracked read, nothing changed")]
    public Task<SaveResult> MongoFlowTrackedUnchanged() => TrackedAsync(_ => { });

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _plain.Dispose();
        _tracked.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<SaveResult> TrackedAsync(Action<Order> change)
    {
        await using var scope = _tracked.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        foreach (var order in await (await vault.Orders.FindAsync(_ => true)).ToListAsync())
        {
            change(order);
        }

        return await vault.SaveAsync();
    }
}
