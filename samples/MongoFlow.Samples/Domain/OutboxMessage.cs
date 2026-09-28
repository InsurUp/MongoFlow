using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

public sealed class OutboxMessage
{
    public ObjectId Id { get; set; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? PublishedAt { get; set; }
}
