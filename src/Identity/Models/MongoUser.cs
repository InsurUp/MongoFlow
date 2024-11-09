using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;

namespace AspNetCore.Identity.MongoFlow;

public class MongoUser : MongoUser<ObjectId>;

public class MongoUser<TKey> : IdentityUser<TKey> where TKey : IEquatable<TKey>
{
    public ICollection<TKey> Roles { get; set; } = new List<TKey>();
    public ICollection<IdentityUserClaim<TKey>> Claims { get; set; } = new List<IdentityUserClaim<TKey>>();
    public ICollection<IdentityUserLogin<TKey>> Logins { get; set; } = new List<IdentityUserLogin<TKey>>();
    public ICollection<IdentityUserToken<TKey>> Tokens { get; set; } = new List<IdentityUserToken<TKey>>();
}