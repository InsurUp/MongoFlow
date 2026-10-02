using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>Service providers with a <see cref="ShopVault"/> registered, without features or with all three built in.</summary>
public static class Vaults
{
    /// <summary>A client of a server that isn't there, for what MongoFlow does before it talks to the server.</summary>
    public static IMongoClient Offline { get; } = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500");

    public static ServiceProvider Plain(IMongoDatabase database) => Build(database, _ => { });

    public static ServiceProvider WithFeatures(IMongoDatabase database) => Build(database, AddFeatures);

    public static ServiceProvider Build(IMongoDatabase database,
        Action<IVaultBuilder<ShopVault>> configure) =>
        new ServiceCollection()
            .AddSingleton(database.Client)
            .AddSingleton(database)
            .AddScoped<Tenant>()
            .AddScoped<OrderRepository>()
            .AddMongoVault<ShopVault>(vault =>
            {
                vault.UseDatabase(database);
                configure(vault);
            })
            .BuildServiceProvider();

    public static void AddFeatures(IVaultBuilder<ShopVault> vault) => vault
        .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
        .UseConcurrencyToken((IVersioned x) => x.Version)
        .UseMultiTenancy((ITenantOwned x) => x.TenantId, services => services.GetRequiredService<Tenant>().Id);
}
