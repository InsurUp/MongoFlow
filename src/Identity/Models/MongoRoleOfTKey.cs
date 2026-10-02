using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>An Identity role stored as one document, with its claims inside.</summary>
public class MongoRole<TKey> : IdentityRole<TKey> where TKey : IEquatable<TKey>
{
    public ICollection<IdentityRoleClaim<TKey>> Claims { get; set; } = new List<IdentityRoleClaim<TKey>>();
}
