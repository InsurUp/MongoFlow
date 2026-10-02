namespace MongoFlow.Identity;

/// <summary>An Identity vault with the app's users, plain roles, and keys of type <typeparamref name="TKey"/>.</summary>
public abstract class IdentityMongoVault<TUser, TKey> : IdentityMongoVault<TUser, MongoRole<TKey>, TKey>
    where TUser : MongoUser<TKey>
    where TKey : IEquatable<TKey>;
