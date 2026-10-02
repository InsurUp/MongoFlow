using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What a read pays for its query filters before it runs: <c>QueryAsync</c> resolving and joining them, against the
/// driver's <c>AsQueryable</c> with the same filter written in. Each returns the query's expression, what the driver
/// translates once the query runs; translating and running it is the driver's in both, so it isn't measured.
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmarks : IDisposable
{
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private ShopVault _vault = null!;
    private Tenant _tenant = null!;
    private IMongoCollection<Order> _orders = null!;

    [ParamsAllValues]
    public FilterKind Filter { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var database = Vaults.Offline.GetDatabase("benchmarks");
        _services = Vaults.Build(database, Configure);
        _scope = _services.CreateScope();
        _vault = _scope.ServiceProvider.GetRequiredService<ShopVault>();
        _tenant = _scope.ServiceProvider.GetRequiredService<Tenant>();
        _orders = database.GetCollection<Order>("Orders");
    }

    [Benchmark(Baseline = true, Description = "Driver: AsQueryable")]
    public async ValueTask<Expression> Driver()
    {
        var query = _orders.AsQueryable();

        switch (Filter)
        {
            case FilterKind.Static:
                return query.Where(x => !x.IsDeleted).Expression;

            case FilterKind.PerQuery or FilterKind.Tenant:
                return query.Where(x => x.TenantId == _tenant.Id).Expression;

            case FilterKind.Async:
                var tenant = await ValueTask.FromResult(_tenant.Id);
                return query.Where(x => x.TenantId == tenant).Expression;

            default:
                return query.Expression;
        }
    }

    [Benchmark(Description = "MongoFlow: QueryAsync")]
    public async ValueTask<Expression> MongoFlow()
    {
        var orders = Filter == FilterKind.FeatureOff ? _vault.Orders.Without(SoftDeleteFeature.Key) : _vault.Orders;

        return (await orders.QueryAsync()).Expression;
    }

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Configure(IVaultBuilder<ShopVault> vault)
    {
        switch (Filter)
        {
            case FilterKind.Static or FilterKind.FeatureOff:
                vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted);
                break;

            case FilterKind.PerQuery:
                vault.QueryFilter<ITenantOwned>(services => x => x.TenantId == services.GetRequiredService<Tenant>().Id);
                break;

            case FilterKind.Async:
                vault.QueryFilter<ITenantOwned>((services, _) =>
                    ValueTask.FromResult<Expression<Func<ITenantOwned, bool>>>(x => x.TenantId == services.GetRequiredService<Tenant>().Id));
                break;

            case FilterKind.Tenant:
                vault.UseMultiTenancy((ITenantOwned x) => x.TenantId, services => services.GetRequiredService<Tenant>().Id);
                break;
        }
    }
}
