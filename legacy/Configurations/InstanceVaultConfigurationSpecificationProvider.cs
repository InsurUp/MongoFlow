namespace MongoFlow;

internal sealed class InstanceVaultConfigurationSpecificationProvider : IVaultConfigurationSpecificationProvider
{
    private readonly IVaultConfigurationSpecification _specification;

    public InstanceVaultConfigurationSpecificationProvider(IVaultConfigurationSpecification specification)
    {
        _specification = specification;
    }

    public IVaultConfigurationSpecification Get(IServiceProvider serviceProvider)
    {
        return _specification;
    }
}
