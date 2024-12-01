namespace MongoFlow;

internal sealed class StaticVaultInterceptorProvider : IVaultInterceptorProvider
{
    private readonly VaultInterceptor _interceptor;

    public StaticVaultInterceptorProvider(VaultInterceptor interceptor,
        string? name)
    {
        _interceptor = interceptor;
        Name = name;
    }

    public string? Name { get; }

    public VaultInterceptor GetInterceptor(IServiceProvider serviceProvider)
    {
        return _interceptor;
    }
}
