using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

/// <summary>Keyed by its number, stored under an element name of its own.</summary>
public sealed class Policy
{
    public int Id { get; set; }

    [BsonElement("number")]
    public string Number { get; set; } = "";

    public string Holder { get; set; } = "";
}
