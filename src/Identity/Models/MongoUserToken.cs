using MongoDB.Bson;

namespace MongoFlow.Identity;

/// <summary>A user's authentication token with an <see cref="ObjectId"/> key.</summary>
public class MongoUserToken : MongoUserToken<ObjectId>;
