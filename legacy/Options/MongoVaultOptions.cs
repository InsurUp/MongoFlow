namespace MongoFlow;

internal sealed record MongoVaultOptions(List<IVaultConfigurationSpecificationProvider> SpecificationProviders, 
    MongoMigrationOptions? MigrationOptions = null);