using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

public sealed class MongoMigrationOptionsBuilder<TVault> where TVault : MongoVault
{
    private readonly IServiceCollection _services;
    private string _collectionName = "migrations";

    internal MongoMigrationOptionsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public MongoMigrationOptionsBuilder<TVault> SetCollectionName(string collectionName)
    {
        _collectionName = collectionName;
        return this;
    }
    
    public MongoMigrationOptionsBuilder<TVault> MigrateOnStartup()
    {
        _services.AddHostedService<MongoMigrationStartupRunner<TVault>>();
        
        return this;
    }

    internal MongoMigrationOptions Build()
    {
        return new MongoMigrationOptions(_collectionName);
    }
    
}