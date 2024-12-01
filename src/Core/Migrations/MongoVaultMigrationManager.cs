using System.Collections.Frozen;
using System.Reflection;
using Semver;

// ReSharper disable StaticMemberInGenericType

namespace MongoFlow;

internal sealed class MongoVaultMigrationManager<TVault> : IMongoVaultMigrationManager where TVault : MongoVault
{
    private static readonly FrozenSet<IMongoMigration> Migrations;
    private static readonly SemVersion? VaultVersion;
    
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<MigrationDocument> _collection;

    public MongoVaultMigrationManager(MongoMigrationOptions options,
        VaultConfigurationManager<TVault> configurationManager)
    {
        _database = configurationManager.Configuration.Database;
        _collection = _database.GetCollection<MigrationDocument>(options.CollectionName);
    }
    
    static MongoVaultMigrationManager()
    {
        Migrations = typeof(TVault).Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(IMongoMigration)))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(Activator.CreateInstance)
            .OfType<IMongoMigration>()
            .OrderBy(x => x.Version, SemVersion.SortOrderComparer)
            .ToFrozenSet();
        
        var vaultVersionText = typeof(TVault).GetCustomAttribute<MongoVersionAttribute>()?.Version;
        VaultVersion = vaultVersionText is not null ? SemVersion.Parse(vaultVersionText) : null;
    }
    
    public async Task<MigrateResult> MigrateAsync(SemVersion? version = null, CancellationToken cancellationToken = default)
    {
        if (Migrations.Count == 0)
        {
            return MigrateResult.Succeeded(null, []);
        }
        
        version ??= VaultVersion;
        
        var currentVersion = await GetCurrentVersionAsync(cancellationToken);
        if (currentVersion is not null && currentVersion == version)
        {
            return MigrateResult.Succeeded(currentVersion, []);
        }

        var runUp = version is null || currentVersion is null || currentVersion.CompareSortOrderTo(version) < 0;
        
        var migrations = Migrations
            .Where(x => runUp ? 
                currentVersion is null || x.Version.CompareSortOrderTo(currentVersion) > 0 && x.Version.CompareSortOrderTo(version) <= 0 : 
                x.Version.CompareSortOrderTo(currentVersion) <= 0 && x.Version.CompareSortOrderTo(version) > 0)
            .OrderBy(x => x.Version, SemVersion.SortOrderComparer)
            .ToArray();
        
        if (migrations.Length == 0)
        {
            return MigrateResult.Succeeded(currentVersion, []);
        }
        
        var mongoClient = _database.Client;

        using var session = await mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        
        session.StartTransaction();
        
        try
        {
            foreach (var migration in migrations)
            {
                if (runUp)
                {
                    await migration.Up(_database, session, cancellationToken);
                    
                    await _collection.InsertOneAsync(session, new MigrationDocument
                    {
                        Id = ObjectId.GenerateNewId(),
                        Version = migration.Version,
                        Description = migration.Description,
                        Timestamp = DateTime.UtcNow,
                        Name = migration.GetType().Name
                    }, cancellationToken: cancellationToken);
                }
                else
                {
                    await migration.Down(_database, session, cancellationToken);
                    
                    await _collection.DeleteOneAsync(session, 
                        x => x.Version == migration.Version, 
                        cancellationToken: cancellationToken);
                }
            }
            
            await session.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return MigrateResult.Failed(migrations[^1].Version, ex);
        }
        
        return MigrateResult.Succeeded(migrations[^1].Version, migrations);
    }

    public async Task<SemVersion?> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
    {
        var migrations = await _collection
            .Find(FilterDefinition<MigrationDocument>.Empty)
            .ToListAsync(cancellationToken);
        
        if (migrations.Count == 0)
        {
            return null;
        }
        
        var lastMigration = migrations
            .OrderBy(x => x.Version, SemVersion.SortOrderComparer)
            .Last();

        return lastMigration.Version;
    }
}