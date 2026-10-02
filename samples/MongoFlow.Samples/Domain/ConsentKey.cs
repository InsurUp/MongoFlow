using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

/// <summary>A customer gives consent once per purpose, so consents are looked up by both together.</summary>
public readonly record struct ConsentKey(ObjectId CustomerId, ConsentPurpose Purpose);
