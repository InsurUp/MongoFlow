using Semver;

namespace MongoFlow;

public interface IMongoMigration
{
    SemVersion Version { get; }

    string? Description => null;
    
    Task Up(IMongoDatabase db, IClientSessionHandle session, CancellationToken cancellationToken);
    
    Task Down(IMongoDatabase db, IClientSessionHandle session, CancellationToken cancellationToken);
}