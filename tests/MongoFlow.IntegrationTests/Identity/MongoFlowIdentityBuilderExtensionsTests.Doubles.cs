using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Users and roles Identity accepts but MongoFlow's stores don't, a user the vault doesn't store, and an app's managers.
public partial class MongoFlowIdentityBuilderExtensionsTests
{
    public sealed class PlainUser : IdentityUser<ObjectId>;

    public sealed class PlainRole : IdentityRole<ObjectId>;

    public sealed class OtherUser : MongoUser;

    /// <summary>An app's own user manager, registered with <c>AddUserManager</c>.</summary>
    public sealed class AppUserManager(IUserStore<MongoUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<MongoUser> passwordHasher,
        IEnumerable<IUserValidator<MongoUser>> userValidators,
        IEnumerable<IPasswordValidator<MongoUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<MongoUser>> logger)
        : UserManager<MongoUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer,
            errors, services, logger);

    /// <summary>An app's own role manager, registered with <c>AddRoleManager</c>.</summary>
    public sealed class AppRoleManager(IRoleStore<MongoRole> store,
        IEnumerable<IRoleValidator<MongoRole>> roleValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        ILogger<RoleManager<MongoRole>> logger)
        : RoleManager<MongoRole>(store, roleValidators, keyNormalizer, errors, logger);
}
