namespace MongoFlow;

internal interface IVaultConfigurationSpecificationProvider
{
    IVaultConfigurationSpecification Get(IServiceProvider serviceProvider);
}
