namespace MongoFlow;

internal sealed class MongoVaultOptions
{
    public MongoVaultOptions(List<IVaultConfigurationSpecificationProvider> specificationProviders)
    {
        SpecificationProviders = specificationProviders;
    }

    public List<IVaultConfigurationSpecificationProvider> SpecificationProviders { get; }
}
