using Semver;

namespace MongoFlow;

public readonly struct MigrateResult
{
    private MigrateResult(bool success,
        SemVersion? version,
        IMongoMigration[] appliedMigrations,
        Exception? exception)
    {
        Success = success;
        Version = version;
        AppliedMigrations = appliedMigrations;
        Exception = exception;
    }
    
    public bool Success { get; }
    
    public SemVersion? Version { get; }
    
    public IMongoMigration[] AppliedMigrations { get; }
    
    public Exception? Exception { get; }
    
    
    internal static MigrateResult Succeeded(SemVersion? version,
        IMongoMigration[] appliedMigrations)
    {
        return new MigrateResult(true, version, appliedMigrations, null);
    }
    
    internal static MigrateResult Failed(SemVersion version,
        Exception exception)
    {
        return new MigrateResult(false, version, [], exception);
    }
}