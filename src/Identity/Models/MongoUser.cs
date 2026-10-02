using MongoDB.Bson;

namespace MongoFlow.Identity;

/// <summary>An Identity user with an <see cref="ObjectId"/> key.</summary>
public class MongoUser : MongoUser<ObjectId>;
