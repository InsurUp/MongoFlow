using MongoDB.Bson;

// Stands in for a separate identity package built on MongoFlow, the way MongoFlow.Identity is.
namespace MongoFlow.Samples.Identity;

public abstract class UserAccount
{
    public ObjectId Id { get; set; }

    public required string UserName { get; set; }

    public required string NormalizedEmail { get; set; }

    public string? PasswordHash { get; set; }
}

/// <summary>Login tokens are looked up by user and provider together.</summary>
public readonly record struct TokenKey(ObjectId UserId, string Provider);

public sealed class UserToken
{
    public ObjectId Id { get; set; }

    public ObjectId UserId { get; set; }

    public required string Provider { get; set; }

    public required string Value { get; set; }

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// A vault base class the package ships. Apps derive from it with their own user type, passing their vault as
/// <typeparamref name="TSelf"/>, and the base configures every derived vault: nothing to apply at registration.
/// </summary>
public abstract class UserVault<TSelf, TUser> : MongoVault, IConfigurableVault<TSelf>
    where TSelf : UserVault<TSelf, TUser>
    where TUser : UserAccount
{
    public IVaultCollection<TUser, ObjectId> Users { get; init; } = null!;

    public IVaultCollection<UserToken, TokenKey> Tokens { get; init; } = null!;

    public static void Configure(IVaultBuilder<TSelf> vault) => vault
        .Collection(x => x.Users, users => users
            .Name("users")
            .Index(i => i.Ascending(u => u.NormalizedEmail), o => o.Unique = true))
        .Collection(x => x.Tokens, tokens => tokens
            .Name("user_tokens")
            .Key(t => new TokenKey(t.UserId, t.Provider)) // composite; also declares its unique index
            .Index(i => i.Ascending(t => t.ExpiresAt), o => o.ExpireAfter = TimeSpan.Zero));
}
