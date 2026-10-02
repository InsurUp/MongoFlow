using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Benchmarks;

/// <summary>A document keyed by two members together.</summary>
public sealed class LoginToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";

    [BsonElement("provider")]
    public string Provider { get; set; } = "";
}
