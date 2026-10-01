using MongoDB.Bson;

// Stands in for a separate identity package built on MongoFlow, the way MongoFlow.Identity is.
namespace MongoFlow.Samples.Identity;

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
        .Collection(x => x.Users, users => users.Name("users"))
        .Collection(x => x.Tokens, tokens => tokens
            .Name("user_tokens")
            .Key(t => new TokenKey(t.UserId, t.Provider))); // composite
}
