using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>A user's authentication token, stored as a document of its own and keyed by <see cref="Id"/>.</summary>
public class MongoUserToken<TKey> : IdentityUserToken<TKey> where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; } = default!;
}
