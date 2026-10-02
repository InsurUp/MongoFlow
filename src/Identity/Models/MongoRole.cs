using MongoDB.Bson;

namespace MongoFlow.Identity;

/// <summary>An Identity role with an <see cref="ObjectId"/> key.</summary>
public class MongoRole : MongoRole<ObjectId>;
