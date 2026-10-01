using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What saving updates by key costs over the driver writing them itself: by key from a vault resolved for the request;
/// with the built-in features, whose query filters every write carries and whose token every update increments; and
/// made with the documents read, which the concurrency token checks. Every save increments each document's total, so
/// saves repeat on the documents seeded once.
/// </summary>
[MemoryDiagnoser]
public class UpdateByKeyBenchmarks : IDisposable
{
    private static readonly UpdateDefinition<Order> Increment = Builders<Order>.Update.Inc(x => x.Total, 1);

    private BenchmarkDatabase _database = null!;
    private ServiceProvider _plain = null!;
    private ServiceProvider _features = null!;
    private List<Order> _read = null!;

    [DocumentCountParams]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = new BenchmarkDatabase();
        _database.Seed(Count);
        _read = await _database.Orders.Find(FilterDefinition<Order>.Empty).SortBy(x => x.Id).ToListAsync();
        _plain = Vaults.Plain(_database.Database);
        _features = Vaults.WithFeatures(_database.Database);

        // The first save asks the server whether it supports client bulk writes; that isn't measured.
        await MongoFlow();
    }

    [Benchmark(Baseline = true, Description = "Driver: client bulk write")]
    public Task<ClientBulkWriteResult> Driver()
    {
        var models = new BulkWriteModel[Count];
        for (var i = 0; i < Count; i++)
        {
            models[i] = new BulkWriteUpdateOneModel<Order>(_database.Orders.CollectionNamespace,
                Builders<Order>.Filter.Eq(x => x.Id, i + 1),
                Increment);
        }

        return ClientBulkWrites.WriteAsync(_database.Client, models);
    }

    [Benchmark(Description = "MongoFlow: UpdateByKey")]
    public Task<SaveResult> MongoFlow() => SaveAsync(_plain, (orders, i) => orders.UpdateByKey(i + 1, Increment));

    [Benchmark(Description = "MongoFlow: UpdateByKey with features")]
    public Task<SaveResult> MongoFlowWithFeatures() => SaveAsync(_features, (orders, i) => orders.UpdateByKey(i + 1, Increment));

    [Benchmark(Description = "MongoFlow: Update, token checked")]
    public Task<SaveResult> MongoFlowTokenChecked() => SaveAsync(_features, (orders, i) => orders.Update(_read[i], Increment));

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _plain.Dispose();
        _features.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<SaveResult> SaveAsync(ServiceProvider services,
        Action<IVaultCollection<Order, int>, int> queue)
    {
        await using var scope = services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        for (var i = 0; i < Count; i++)
        {
            queue(vault.Orders, i);
        }

        return await vault.SaveAsync();
    }
}
