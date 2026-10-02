namespace MongoFlow.Identity;

/// <summary>
/// A vault with ASP.NET Core Identity's collections. Derive the app's vault from it, register it with
/// <c>AddMongoVault</c>, and pass it to <see cref="MongoFlowIdentityBuilderExtensions.AddMongoFlowStores{TVault}"/>.
/// </summary>
/// <remarks>
/// Each collection is named after its property and keyed by its documents' <c>Id</c>, unless the app's configuration says
/// otherwise. The vault's features apply to them like to any other, and <see cref="UserManagerExtensions.Without{TUser}"/>
/// switches one off.
/// </remarks>
public abstract class IdentityMongoVault<TUser, TRole, TKey> : MongoVault
    where TUser : MongoUser<TKey>
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>Users, with their claims, logins, roles and passkeys inside.</summary>
    public IVaultCollection<TUser, TKey> Users { get; init; } = null!;

    /// <summary>Roles, with their claims inside.</summary>
    public IVaultCollection<TRole, TKey> Roles { get; init; } = null!;

    /// <summary>Users' authentication tokens, such as an authenticator key or recovery codes.</summary>
    public IVaultCollection<MongoUserToken<TKey>, TKey> UserTokens { get; init; } = null!;
}
