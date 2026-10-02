using MongoDB.Bson;

namespace MongoFlow.Identity;

/// <summary>An Identity vault with the app's users, plain roles, and <see cref="ObjectId"/> keys.</summary>
public abstract class IdentityMongoVault<TUser> : IdentityMongoVault<TUser, MongoRole, ObjectId>
    where TUser : MongoUser;
