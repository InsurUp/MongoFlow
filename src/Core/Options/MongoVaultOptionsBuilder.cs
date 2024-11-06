namespace MongoFlow;

public sealed class MongoVaultOptionsBuilder
{
    private readonly List<IVaultConfigurationSpecificationProvider> _specificationProviders = [];

    internal MongoVaultOptionsBuilder()
    {
    }

    public MongoVaultOptionsBuilder AddSpecification<TSpecification>() where TSpecification : IVaultConfigurationSpecification
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(typeof(TSpecification), null));
        return this;
    }

    public MongoVaultOptionsBuilder AddSpecification<TSpecification>(params object[] args)
        where TSpecification : IVaultConfigurationSpecification
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(typeof(TSpecification), args));
        return this;
    }

    public MongoVaultOptionsBuilder AddSpecification(IVaultConfigurationSpecification specification)
    {
        _specificationProviders.Add(new InstanceVaultConfigurationSpecificationProvider(specification));
        return this;
    }

    public MongoVaultOptionsBuilder AddSpecification(Type specificationType)
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(specificationType, null));
        return this;
    }

    internal MongoVaultOptions Build()
    {
        return new MongoVaultOptions(_specificationProviders);
    }
}
