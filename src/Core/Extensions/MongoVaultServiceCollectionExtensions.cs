using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

public static class MongoVaultServiceCollectionExtensions
{
    public static IServiceCollection AddMongoVault<TVault>(this IServiceCollection services,
        Action<MongoVaultOptionsBuilder>? optionsAction = null) where TVault : MongoVault
    {
        var optionsBuilder = new MongoVaultOptionsBuilder();

        optionsAction?.Invoke(optionsBuilder);

        var options = optionsBuilder.Build();

        services.AddScoped<VaultConfigurationManager<TVault>>(serviceProvider =>
            new VaultConfigurationManager<TVault>(serviceProvider.GetRequiredService<VaultConfigurationProvider<TVault>>(), serviceProvider));
        services.AddSingleton(new VaultConfigurationProvider<TVault>(options));
        services.AddScoped<TVault>();

        return services;
    }

    public static IServiceCollection AddMongoVault<TInterface, TVault>(this IServiceCollection services,
        Action<MongoVaultOptionsBuilder>? optionsAction = null) where TVault : MongoVault, TInterface where TInterface : class
    {
        var optionsBuilder = new MongoVaultOptionsBuilder();

        optionsAction?.Invoke(optionsBuilder);

        var options = optionsBuilder.Build();

        services.AddScoped<VaultConfigurationManager<TVault>>(serviceProvider =>
            new VaultConfigurationManager<TVault>(serviceProvider.GetRequiredService<VaultConfigurationProvider<TVault>>(), serviceProvider));
        services.AddSingleton(new VaultConfigurationProvider<TVault>(options));
        services.AddScoped<TVault>();
        services.AddScoped<TInterface, TVault>(serviceProvider => serviceProvider.GetRequiredService<TVault>());

        return services;
    }
    
    public static IServiceCollection AddMongoGlobalTransaction(this IServiceCollection services, Func<IServiceProvider, MongoClient> mongoClientFactory)
    {
        services.AddScoped<IMongoGlobalTransactionManager>(serviceProvider =>
            new MongoGlobalTransactionManager(mongoClientFactory(serviceProvider)));

        return services;
    }
}
