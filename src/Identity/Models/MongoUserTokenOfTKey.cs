using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>A user's authentication token, stored as a document of its own and keyed by <see cref="Id"/>.</summary>
public class MongoUserToken<TKey> : IdentityUserToken<TKey> where TKey : IEquatable<TKey>
{
    /// <summary>The token's key, which the driver or, with <see cref="string"/> keys, the store gives it.</summary>
    public TKey Id { get; set; } = default!;
}
