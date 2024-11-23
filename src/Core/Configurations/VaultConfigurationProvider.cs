namespace MongoFlow;

internal sealed class VaultConfigurationProvider<TVault> where TVault : MongoVault
{
    private readonly MongoVaultOptions _options;
    private VaultConfiguration? _configuration;

    public VaultConfigurationProvider(MongoVaultOptions options)
    {
        _options = options;
    }

    public VaultConfiguration GetConfiguration(IServiceProvider serviceProvider)
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        var builder = new VaultConfigurationBuilder(typeof(TVault));

        foreach (var specificationType in _options.SpecificationProviders)
        {
            var specification = specificationType.Get(serviceProvider);
            specification.Configure(builder);
        }

        _configuration = builder.Build();

        return _configuration;
    }
    
    public MongoMigrationOptions? MigrationOptions => _options.MigrationOptions;
}
