using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;

namespace MongoFlow.Identity;

public class MongoRole : MongoRole<ObjectId>;

public class MongoRole<TKey> : IdentityRole<TKey> where TKey : IEquatable<TKey>
{
    public ICollection<IdentityRoleClaim<TKey>> Claims { get; set; } = new List<IdentityRoleClaim<TKey>>();
}