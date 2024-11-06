namespace MongoFlow;

internal sealed class StaticVaultInterceptorProvider : IVaultInterceptorProvider
{
    private readonly VaultInterceptor _interceptor;

    public StaticVaultInterceptorProvider(VaultInterceptor interceptor)
    {
        _interceptor = interceptor;
    }

    public VaultInterceptor GetInterceptor(IServiceProvider serviceProvider)
    {
        return _interceptor;
    }
}
