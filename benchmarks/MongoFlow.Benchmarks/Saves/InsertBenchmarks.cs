using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What saving inserts costs over the driver writing them itself, as the same ordered, verbose client bulk write in a
/// transaction: from a vault resolved for the request, then with the three built-in features, which stamp each
/// insert's tenant. The collection is emptied after each iteration, so it doesn't grow through the run.
/// </summary>
[MemoryDiagnoser]
public class InsertBenchmarks : IDisposable
{
    private BenchmarkDatabase _database = null!;
    private ServiceProvider _plain = null!;
    private ServiceProvider _features = null!;
    private int _lastId;

    [DocumentCountParams]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = new BenchmarkDatabase();
        _plain = Vaults.Plain(_database.Database);
        _features = Vaults.WithFeatures(_database.Database);

        // The first save asks the server whether it supports client bulk writes; that isn't measured.
        await SaveAsync(_plain);
    }

    [IterationCleanup]
    public void Empty() => _database.Orders.DeleteMany(FilterDefinition<Order>.Empty);

    [Benchmark(Baseline = true, Description = "Driver: client bulk write")]
    public Task<ClientBulkWriteResult> Driver()
    {
        var models = new BulkWriteModel[Count];
        for (var i = 0; i < Count; i++)
        {
            models[i] = new BulkWriteInsertOneModel<Order>(_database.Orders.CollectionNamespace, NewOrder());
        }

        return ClientBulkWrites.WriteAsync(_database.Client, models);
    }

    [Benchmark(Description = "MongoFlow: save")]
    public Task<SaveResult> MongoFlow() => SaveAsync(_plain);

    [Benchmark(Description = "MongoFlow: save with features")]
    public Task<SaveResult> MongoFlowWithFeatures() => SaveAsync(_features);

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _plain.Dispose();
        _features.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<SaveResult> SaveAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        for (var i = 0; i < Count; i++)
        {
            vault.Orders.Add(NewOrder());
        }

        return await vault.SaveAsync();
    }

    private Order NewOrder() => new() { Id = ++_lastId, Customer = "ada", Total = 10 };
}
