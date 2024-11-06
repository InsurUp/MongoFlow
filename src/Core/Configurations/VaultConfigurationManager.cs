namespace MongoFlow;

public abstract class VaultConfigurationManager
{
    internal abstract VaultConfiguration Configuration { get; }

    internal abstract VaultInterceptor[] ResolveInterceptors();

    internal abstract IServiceProvider ServiceProvider { get; }
}

public class VaultConfigurationManager<TVault> : VaultConfigurationManager where TVault : MongoVault
{
    internal VaultConfigurationManager(VaultConfigurationProvider<TVault> configurationProvider,
        IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Configuration = configurationProvider.GetConfiguration(serviceProvider);
    }

    internal override IServiceProvider ServiceProvider { get; }

    internal override VaultConfiguration Configuration { get; }

    internal override VaultInterceptor[] ResolveInterceptors()
    {
        return Configuration.Interceptors
            .Select(interceptorDefinition => interceptorDefinition.GetInterceptor(ServiceProvider))
            .ToArray();
    }
}
