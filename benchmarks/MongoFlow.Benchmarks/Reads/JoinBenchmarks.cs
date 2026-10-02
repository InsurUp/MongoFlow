using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What a join on another collection's query pays before it runs: each order grouped with its customer's other orders,
/// soft deleted ones left out on both sides. MongoFlow joins two <c>QueryAsync</c> queries and rewrites the join as the
/// driver's <c>Lookup</c>; the driver writes that <c>Lookup</c> itself, with the filter written in. Each returns the
/// query's expression, the same in both; translating and running it is the driver's, so it isn't measured.
/// </summary>
[MemoryDiagnoser]
public class JoinBenchmarks : IDisposable
{
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private ShopVault _vault = null!;
    private IMongoCollection<Order> _orders = null!;

    [GlobalSetup]
    public void Setup()
    {
        var database = Vaults.Offline.GetDatabase("benchmarks");
        _services = Vaults.Build(database, vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted));
        _scope = _services.CreateScope();
        _vault = _scope.ServiceProvider.GetRequiredService<ShopVault>();
        _orders = database.GetCollection<Order>("Orders");
    }

    [Benchmark(Baseline = true, Description = "Driver: Lookup")]
    public Expression Driver() =>
        _orders.AsQueryable()
            .Where(x => !x.IsDeleted)
            .Lookup(_orders, order => order.Customer, other => other.Customer, (_, others) => others.Where(x => !x.IsDeleted))
            .Select(x => new { x.Local.Id, Others = x.Results.Length })
            .Expression;

    [Benchmark(Description = "MongoFlow: join on QueryAsync")]
    public async ValueTask<Expression> MongoFlow() =>
        (from order in await _vault.Orders.QueryAsync()
         join other in await _vault.Orders.QueryAsync() on order.Customer equals other.Customer into others
         select new { order.Id, Others = others.Count() }).Expression;

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        GC.SuppressFinalize(this);
    }
}
