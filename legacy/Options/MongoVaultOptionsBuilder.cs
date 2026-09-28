using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

public sealed class MongoVaultOptionsBuilder<TVault> where TVault : MongoVault
{
    private readonly IServiceCollection _services;
    private readonly List<IVaultConfigurationSpecificationProvider> _specificationProviders = [];
    private MongoMigrationOptions? _migrationOptions;

    internal MongoVaultOptionsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public MongoVaultOptionsBuilder<TVault> AddSpecification<TSpecification>() where TSpecification : IVaultConfigurationSpecification
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(typeof(TSpecification), null));
        return this;
    }

    public MongoVaultOptionsBuilder<TVault> AddSpecification<TSpecification>(params object[] args)
        where TSpecification : IVaultConfigurationSpecification
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(typeof(TSpecification), args));
        return this;
    }

    public MongoVaultOptionsBuilder<TVault> AddSpecification(IVaultConfigurationSpecification specification)
    {
        _specificationProviders.Add(new InstanceVaultConfigurationSpecificationProvider(specification));
        return this;
    }

    public MongoVaultOptionsBuilder<TVault> AddSpecification(Type specificationType)
    {
        _specificationProviders.Add(new TypeVaultConfigurationSpecificationProvider(specificationType, null));
        return this;
    }
    
    public MongoVaultOptionsBuilder<TVault> EnableMigration(Action<MongoMigrationOptionsBuilder<TVault>>? migrationOptionsAction = null)
    {
        var builder = new MongoMigrationOptionsBuilder<TVault>(_services);
        
        migrationOptionsAction?.Invoke(builder);
        
        _migrationOptions = builder.Build();
        
        return this;
    }

    internal MongoVaultOptions Build()
    {
        return new MongoVaultOptions(_specificationProviders, _migrationOptions);
    }
}
