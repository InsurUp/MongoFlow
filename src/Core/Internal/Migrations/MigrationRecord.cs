using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Semver;

namespace MongoFlow;

/// <summary>An applied migration, in the format earlier MongoFlow versions wrote, so existing history carries over.</summary>
[BsonIgnoreExtraElements]
internal sealed class MigrationRecord
{
    public ObjectId Id { get; init; }

    [BsonSerializer(typeof(SemVersionSerializer))]
    public required SemVersion Version { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required DateTime Timestamp { get; init; }
}
