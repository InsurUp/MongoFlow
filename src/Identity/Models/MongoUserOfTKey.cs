using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>An Identity user stored as one document, with its claims, logins, role ids and passkeys inside.</summary>
public class MongoUser<TKey> : IdentityUser<TKey> where TKey : IEquatable<TKey>
{
    /// <summary>The ids of the user's roles.</summary>
    public ICollection<TKey> Roles { get; set; } = new List<TKey>();

    /// <summary>The user's claims.</summary>
    public ICollection<IdentityUserClaim<TKey>> Claims { get; set; } = new List<IdentityUserClaim<TKey>>();

    /// <summary>The user's logins with external providers.</summary>
    public ICollection<IdentityUserLogin<TKey>> Logins { get; set; } = new List<IdentityUserLogin<TKey>>();

    /// <summary>The user's passkeys.</summary>
    public ICollection<IdentityUserPasskey<TKey>> Passkeys { get; set; } = new List<IdentityUserPasskey<TKey>>();
}
