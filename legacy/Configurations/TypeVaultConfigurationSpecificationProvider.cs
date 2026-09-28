using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

internal sealed class TypeVaultConfigurationSpecificationProvider : IVaultConfigurationSpecificationProvider
{
    private readonly Type _type;
    private readonly object[]? _args;

    public TypeVaultConfigurationSpecificationProvider(Type type, object[]? args)
    {
        _type = type;
        _args = args;
    }

    public IVaultConfigurationSpecification Get(IServiceProvider serviceProvider)
    {
        if (_args is not null)
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, _type, _args) as IVaultConfigurationSpecification ??
                   throw new VaultConfigurationException($"Specified type {_type.FullName} is not a valid specification type.");
        }

        return serviceProvider.GetService(_type) as IVaultConfigurationSpecification ??
               ActivatorUtilities.CreateInstance(serviceProvider, _type) as IVaultConfigurationSpecification ??
               throw new VaultConfigurationException($"Specified type {_type.FullName} is not a valid specification type.");
    }
}
