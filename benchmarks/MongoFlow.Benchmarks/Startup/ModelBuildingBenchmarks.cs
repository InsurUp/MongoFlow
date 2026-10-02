using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What an app pays once per vault, at startup: building the vault's model the first time it's resolved. With the
/// built-in features, each adds its query filters and interceptors and compiles its member accessors. The driver has
/// nothing to compare: the vault without features is the baseline.
/// </summary>
[MemoryDiagnoser]
public class ModelBuildingBenchmarks
{
    private readonly IMongoDatabase _database = Vaults.Offline.GetDatabase("benchmarks");

    [Benchmark(Baseline = true, Description = "MongoFlow: vault")]
    public ShopVault Plain() => FirstVault(Vaults.Plain(_database));

    [Benchmark(Description = "MongoFlow: vault with features")]
    public ShopVault WithFeatures() => FirstVault(Vaults.WithFeatures(_database));

    private static ShopVault FirstVault(ServiceProvider services)
    {
        using (services)
        {
            using var scope = services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ShopVault>();
        }
    }
}
