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

/// <summary>
/// Login tokens, looked up by user and provider together. MongoFlow has no composite keys, so the collection is keyless.
/// </summary>
public sealed class UserToken
{
    public ObjectId UserId { get; set; }

    public required string Provider { get; set; }

    public required string Value { get; set; }

    public DateTime ExpiresAt { get; set; }
}

/// <summary>A vault base class the package ships; apps derive from it with their own user type.</summary>
public abstract class UserVault<TUser> : MongoVault where TUser : UserAccount
{
    public IVaultCollection<TUser, ObjectId> Users { get; init; } = null!;

    public IVaultCollection<UserToken> Tokens { get; init; } = null!;
}

/// <summary>
/// The package's setup. It needs <typeparamref name="TUser"/> as well as <typeparamref name="TVault"/>, so it can't be
/// registered as a default, which takes exactly one type parameter. Apps have to apply it to their vault by hand.
/// </summary>
public sealed class UserVaultConfiguration<TVault, TUser> : IVaultConfiguration<TVault>
    where TVault : UserVault<TUser>
    where TUser : UserAccount
{
    public void Configure(IVaultBuilder<TVault> vault) => vault
        .Collection(x => x.Users, c => c.Name("users"))
        .Collection(x => x.Tokens, c => c.Name("user_tokens"));
}
