using MongoDB.Bson;

// Stands in for a separate identity package built on MongoFlow, the way MongoFlow.Identity is.
namespace MongoFlow.Samples.Identity;

public sealed class UserToken
{
    public ObjectId Id { get; set; }

    public ObjectId UserId { get; set; }

    public required string Provider { get; set; }

    public required string Value { get; set; }

    public DateTime ExpiresAt { get; set; }
}
