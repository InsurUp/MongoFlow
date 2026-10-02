using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Keys other than ObjectId, converted from Identity's strings as Identity converts them.
public partial class MongoUserStoreTests
{
    [Test]
    public async Task FindByIdAsync_StringKeys_FindByTheKeyAndNothingForAnEmptyOne()
    {
        // Arrange
        await using var host = StringKeyHost();
        var users = host.Services.GetRequiredService<UserManager<MongoUser<string>>>();
        var roles = host.Services.GetRequiredService<RoleManager<MongoRole<string>>>();
        await users.CreateAsync(new MongoUser<string> { Id = "u-ada", UserName = "ada" });
        await roles.CreateAsync(new MongoRole<string> { Id = "r-admin", Name = "admin" });

        // Act
        var user = await users.FindByIdAsync("u-ada");
        var noUser = await users.FindByIdAsync("");
        var role = await roles.FindByIdAsync("r-admin");
        var noRole = await roles.FindByIdAsync("");
        var roleId = await roles.GetRoleIdAsync(role!);
        var unsavedRoleId = await roles.GetRoleIdAsync(new MongoRole<string> { Name = "unsaved" });

        // Assert
        await Verify(new
        {
            User = user?.UserName,
            NoUser = noUser?.UserName,
            Role = role?.Name,
            NoRole = noRole?.Name,
            RoleId = roleId,
            UnsavedRoleId = unsavedRoleId
        });
    }

    [Test]
    public async Task SetAndRemoveAuthenticationTokenAsync_StringKeys_KeepEachUsersTokens()
    {
        // Arrange — the store creates tokens, so it gives them their keys.
        await using var host = StringKeyHost();
        var users = host.Services.GetRequiredService<UserManager<MongoUser<string>>>();
        var ada = new MongoUser<string> { Id = "u-ada", UserName = "ada" };
        var bob = new MongoUser<string> { Id = "u-bob", UserName = "bob" };
        await users.CreateAsync(ada);
        await users.CreateAsync(bob);

        // Act
        await users.SetAuthenticationTokenAsync(ada, "app", "refresh", "ada-1");
        await users.SetAuthenticationTokenAsync(bob, "app", "refresh", "bob-1");
        await users.SetAuthenticationTokenAsync(ada, "app", "refresh", "ada-2");
        await users.RemoveAuthenticationTokenAsync(bob, "app", "refresh");

        // Assert
        await Verify(new
        {
            Ada = await users.GetAuthenticationTokenAsync(ada, "app", "refresh"),
            Bob = await users.GetAuthenticationTokenAsync(bob, "app", "refresh")
        });
    }

    private VaultHost<StringKeyVault> StringKeyHost() =>
        Mongo.Host<StringKeyVault>(services: services => services
            .AddIdentityCore<MongoUser<string>>()
            .AddRoles<MongoRole<string>>()
            .AddMongoFlowStores<StringKeyVault>());

    /// <summary>An Identity vault keyed by strings the app chooses.</summary>
    public sealed class StringKeyVault : IdentityMongoVault<MongoUser<string>, string>;
}
