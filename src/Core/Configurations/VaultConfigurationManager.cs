namespace MongoFlow;

public abstract class VaultConfigurationManager
{
    internal abstract VaultConfiguration Configuration { get; }

    internal abstract VaultInterceptor[] ResolveInterceptors();

    internal abstract IServiceProvider ServiceProvider { get; }
    
    public abstract bool MigrationEnabled { get; }
    
    public abstract IMongoVaultMigrationManager MigrationManager { get; }
}

public class VaultConfigurationManager<TVault> : VaultConfigurationManager where TVault : MongoVault
{
    private readonly VaultConfigurationProvider<TVault> _configurationProvider;

    internal VaultConfigurationManager(VaultConfigurationProvider<TVault> configurationProvider,
        IServiceProvider serviceProvider)
    {
        _configurationProvider = configurationProvider;
        ServiceProvider = serviceProvider;
        Configuration = configurationProvider.GetConfiguration(serviceProvider);
    }

    internal override IServiceProvider ServiceProvider { get; }
    
    public override bool MigrationEnabled => _configurationProvider.MigrationOptions is not null;
    
    public override IMongoVaultMigrationManager MigrationManager => MigrationEnabled
        ? new MongoVaultMigrationManager<TVault>(_configurationProvider.MigrationOptions!, this)
        : throw new InvalidOperationException("Migration is not enabled for this vault.");

    internal override VaultConfiguration Configuration { get; }

    internal override VaultInterceptor[] ResolveInterceptors()
    {
        return Configuration.Interceptors
            .Select(interceptorDefinition => interceptorDefinition.GetInterceptor(ServiceProvider))
            .ToArray();
    }
}
