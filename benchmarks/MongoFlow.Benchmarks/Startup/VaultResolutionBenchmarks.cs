using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What every request pays for its vault: resolving it from a new scope, which fills its collection properties,
/// against resolving a repository over the driver's collections. The model is built once, in setup.
/// </summary>
[MemoryDiagnoser]
public class VaultResolutionBenchmarks : IDisposable
{
    private ServiceProvider _services = null!;

    [GlobalSetup]
    public void Setup()
    {
        _services = Vaults.WithFeatures(Vaults.Offline.GetDatabase("benchmarks"));

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ShopVault>();
    }

    [Benchmark(Baseline = true, Description = "Driver: repository")]
    public OrderRepository Repository()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OrderRepository>();
    }

    [Benchmark(Description = "MongoFlow: vault")]
    public ShopVault Vault()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ShopVault>();
    }

    // Not sealed: BenchmarkDotNet runs a class it derives from this one.
    [GlobalCleanup]
    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }
}
