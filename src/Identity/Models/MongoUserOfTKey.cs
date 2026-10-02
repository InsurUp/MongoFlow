using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>An Identity user stored as one document, with its claims, logins, role ids and passkeys inside.</summary>
public class MongoUser<TKey> : IdentityUser<TKey> where TKey : IEquatable<TKey>
{
    public ICollection<TKey> Roles { get; set; } = new List<TKey>();

    public ICollection<IdentityUserClaim<TKey>> Claims { get; set; } = new List<IdentityUserClaim<TKey>>();

    public ICollection<IdentityUserLogin<TKey>> Logins { get; set; } = new List<IdentityUserLogin<TKey>>();

    public ICollection<IdentityUserPasskey<TKey>> Passkeys { get; set; } = new List<IdentityUserPasskey<TKey>>();
}
