using MongoDB.Bson;

// Stands in for a separate identity package built on MongoFlow, the way MongoFlow.Identity is.
namespace MongoFlow.Samples.Identity;

/// <summary>Login tokens are looked up by user and provider together.</summary>
public readonly record struct TokenKey(ObjectId UserId, string Provider);
