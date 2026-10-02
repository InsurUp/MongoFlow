using MongoDB.Bson;

namespace MongoFlow.Identity;

/// <summary>An Identity vault with plain users and roles, and <see cref="ObjectId"/> keys.</summary>
public abstract class IdentityMongoVault : IdentityMongoVault<MongoUser, MongoRole, ObjectId>;
