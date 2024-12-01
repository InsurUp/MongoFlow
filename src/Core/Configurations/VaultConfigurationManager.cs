namespace MongoFlow;

public abstract class VaultConfigurationManager
{
    internal abstract VaultConfiguration Configuration { get; }

    internal abstract (string? Name, VaultInterceptor Interceptor)[] ResolveInterceptors();

    internal abstract IServiceProvider ServiceProvider { get; }
    
    internal abstract bool MigrationEnabled { get; }
    
    internal abstract IMongoVaultMigrationManager MigrationManager { get; }
}

public class VaultConfigurationManager<TVault> : VaultConfigurationManager where TVault : MongoVault
{
    private readonly MongoVaultOptions _options;

    internal VaultConfigurationManager(VaultConfigurationProvider<TVault> configurationProvider,
        MongoVaultOptions options,
        IServiceProvider serviceProvider)
    {
        _options = options;
        ServiceProvider = serviceProvider;
        Configuration = configurationProvider.GetConfiguration(serviceProvider);
    }

    internal override IServiceProvider ServiceProvider { get; }
    
    internal override bool MigrationEnabled => _options.MigrationOptions is not null;
    
    internal override IMongoVaultMigrationManager MigrationManager => MigrationEnabled
        ? new MongoVaultMigrationManager<TVault>(_options.MigrationOptions!, this)
        : throw new InvalidOperationException("Migration is not enabled for this vault.");

    internal override VaultConfiguration Configuration { get; }

    internal override (string? Name, VaultInterceptor Interceptor)[] ResolveInterceptors()
    {
        return Configuration.Interceptors
            .Select(interceptorDefinition => (interceptorDefinition.Name, interceptorDefinition.GetInterceptor(ServiceProvider)))
            .ToArray();
    }
}
