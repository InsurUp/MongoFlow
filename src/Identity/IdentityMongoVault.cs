using MongoDB.Bson;

namespace MongoFlow.Identity;

public abstract class IdentityMongoVault : IdentityMongoVault<MongoUser, MongoRole, ObjectId>
{
    protected IdentityMongoVault(VaultConfigurationManager configurationManager) : base(configurationManager)
    {
    }
}

public abstract class IdentityMongoVault<TUser> : IdentityMongoVault<TUser, MongoRole, ObjectId>
    where TUser : MongoUser
{
    protected IdentityMongoVault(VaultConfigurationManager configurationManager) : base(configurationManager)
    {
    }
}

public abstract class IdentityMongoVault<TUser, TKey> : IdentityMongoVault<TUser, MongoRole<TKey>, TKey>
    where TUser : MongoUser<TKey>
    where TKey : IEquatable<TKey>
{
    protected IdentityMongoVault(VaultConfigurationManager configurationManager) : base(configurationManager)
    {
    }
}

public abstract class IdentityMongoVault<TUser, TRole, TKey> : MongoVault
    where TUser : MongoUser<TKey>
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    protected IdentityMongoVault(VaultConfigurationManager configurationManager) : base(configurationManager)
    {
    }

    public DocumentSet<TUser> Users { get; set; } = null!;
    public DocumentSet<TRole> Roles { get; set; } = null!;
    public DocumentSet<MongoUserToken<TKey>> UserTokens { get; set; } = null!;
}