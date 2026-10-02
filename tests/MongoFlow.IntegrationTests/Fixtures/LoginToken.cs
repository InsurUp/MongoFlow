using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

/// <summary>Keyed by user and provider together.</summary>
public sealed class LoginToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";

    [BsonElement("provider")]
    public string Provider { get; set; } = "";

    public string Value { get; set; } = "";
}
