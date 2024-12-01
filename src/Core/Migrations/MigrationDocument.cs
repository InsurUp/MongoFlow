using Semver;

namespace MongoFlow;

internal sealed class MigrationDocument
{
    public ObjectId Id { get; init; }
    
    public required SemVersion Version { get; init; }
    
    public required string Name { get; init; }
    
    public string? Description { get; init; }
    
    public required DateTime Timestamp { get; init; }
}