using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What a save of soft deletes by key costs over the driver writing the updates soft delete turns them into: each
/// matches the key and a document not yet deleted, and sets the flag. The documents are restored after each
/// iteration, so every save deletes them all again.
/// </summary>
[MemoryDiagnoser]
public class SoftDeleteBenchmarks : IDisposable
{
    private static readonly UpdateDefinition<Order> MarkDeleted = Builders<Order>.Update.Set(x => x.IsDeleted, true);
    private static readonly UpdateDefinition<Order> Restore = Builders<Order>.Update.Set(x => x.IsDeleted, false);

    private BenchmarkDatabase _database = null!;
    private ServiceProvider _services = null!;

    [Params(100, 10_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = new BenchmarkDatabase();
        _database.Seed(Count);
        _services = Vaults.Build(_database.Database, vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));

        // The first save asks the server whether it supports client bulk writes; that isn't measured.
        await MongoFlow();
        RestoreAll();
    }

    [IterationCleanup]
    public void RestoreAll() => _database.Orders.UpdateMany(FilterDefinition<Order>.Empty, Restore);

    [Benchmark(Baseline = true, Description = "Driver: client bulk write")]
    public Task<ClientBulkWriteResult> Driver()
    {
        var models = new BulkWriteModel[Count];
        for (var i = 0; i < Count; i++)
        {
            models[i] = new BulkWriteUpdateOneModel<Order>(_database.Orders.CollectionNamespace,
                Builders<Order>.Filter.Eq(x => x.Id, i + 1) & Builders<Order>.Filter.Ne(x => x.IsDeleted, true),
                MarkDeleted);
        }

        return ClientBulkWrites.WriteAsync(_database.Client, models);
    }

    [Benchmark(Description = "MongoFlow: DeleteByKey")]
    public async Task<SaveResult> MongoFlow()
    {
        await using var scope = _services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        for (var i = 0; i < Count; i++)
        {
            vault.Orders.DeleteByKey(i + 1);
        }

        return await vault.SaveAsync();
    }

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
