using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

internal sealed class ServiceVaultInterceptorProvider : IVaultInterceptorProvider
{
    private readonly Type _type;
    private readonly object[] _args;

    public ServiceVaultInterceptorProvider(Type type,
        string? name,
        object[] args)
    {
        _type = type;
        _args = args;
        Name = name;
    }

    public string? Name { get; }

    public VaultInterceptor GetInterceptor(IServiceProvider serviceProvider)
    {
        return ActivatorUtilities.CreateInstance(serviceProvider, _type, _args) as VaultInterceptor
            ?? throw new InvalidOperationException($"Could not create interceptor of type {_type}");
    }
}
