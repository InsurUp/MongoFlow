using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;

namespace MongoFlow.Identity;

public class MongoUserToken : MongoUserToken<ObjectId>;

public class MongoUserToken<TKey> : IdentityUserToken<TKey> where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; } = default!;
}
