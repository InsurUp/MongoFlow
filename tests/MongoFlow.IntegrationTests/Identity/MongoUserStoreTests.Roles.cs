using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Roles, by the ids stored inside the user, and tokens, stored as documents of their own.
public partial class MongoUserStoreTests
{
    private static readonly ObjectId AdminId = ObjectId.Parse("6600000000000000000000b1");

    [Test]
    public async Task AddToRoleAsync_ExistingRole_StoresItsIdAndFindsTheUserByIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await Roles(host).CreateAsync(new MongoRole { Id = AdminId, Name = "admin" });

        // Act
        await users.AddToRoleAsync(ada, "admin");

        // Assert
        await Verify(new
        {
            IsInRole = await users.IsInRoleAsync(ada, "admin"),
            Roles = await users.GetRolesAsync(ada),
            InRole = (await users.GetUsersInRoleAsync("admin")).Select(user => user.UserName),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("Users"))
        });
    }

    [Test]
    public async Task RemoveFromRoleAsync_UserInTheRole_TakesItOut()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await Roles(host).CreateAsync(new MongoRole { Id = AdminId, Name = "admin" });
        await users.AddToRoleAsync(ada, "admin");

        // Act
        await users.RemoveFromRoleAsync(ada, "admin");

        // Assert
        await Assert.That(await users.IsInRoleAsync(ada, "admin")).IsFalse();
    }

    [Test]
    public async Task AddToRoleAsync_UnknownRole_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act & Assert
        await ThrowsTask(() => users.AddToRoleAsync(ada, "admin")).IgnoreStackTrace();
    }

    [Test]
    public async Task GetUsersInRoleAsync_UnknownRole_FindsNobody()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);

        // Act
        var found = await users.GetUsersInRoleAsync("admin");

        // Assert
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task FindUserLoginAndFindUserRoleAsync_ByTheUsersId_FindWhatTheUserHas()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await Roles(host).CreateAsync(new MongoRole { Id = AdminId, Name = "admin" });
        await users.AddToRoleAsync(ada, "admin");
        await users.AddLoginAsync(ada, new UserLoginInfo("google", "ada-g", "Google"));
        await users.AddLoginAsync(ada, new UserLoginInfo("github", "ada-gh", "GitHub"));
        var store = new ExposedUserStore(host.Vault);
        var nobody = ObjectId.Parse("6600000000000000000000c1");

        // Act
        var login = await store.FindLoginAsync(AdaId, "github", "ada-gh");
        var role = await store.FindRoleOfAsync(AdaId, AdminId);
        var otherRole = await store.FindRoleOfAsync(AdaId, ObjectId.Parse("6600000000000000000000b2"));
        var nobodysLogin = await store.FindLoginAsync(nobody, "github", "ada-gh");
        var nobodysRole = await store.FindRoleOfAsync(nobody, AdminId);

        // Assert
        await Verify(new
        {
            Login = new { login!.UserId, login.ProviderKey },
            Role = role?.RoleId,
            OtherRole = otherRole?.RoleId,
            NobodysLogin = nobodysLogin?.ProviderKey,
            NobodysRole = nobodysRole?.RoleId
        });
    }

    [Test]
    public async Task SetAuthenticationTokenAsync_NewAndThenChanged_StoresOneTokenWithTheLatestValue()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.SetAuthenticationTokenAsync(ada, "authenticator", "key", "first");
        await users.SetAuthenticationTokenAsync(ada, "authenticator", "key", "second");

        // Assert
        await Verify(new
        {
            Value = await users.GetAuthenticationTokenAsync(ada, "authenticator", "key"),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("UserTokens"), withoutIds: true)
        });
    }

    [Test]
    public async Task RemoveAuthenticationTokenAsync_StoredAndThenGone_RemovesIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await users.SetAuthenticationTokenAsync(ada, "authenticator", "key", "first");

        // Act — the second removal finds nothing to remove.
        await users.RemoveAuthenticationTokenAsync(ada, "authenticator", "key");
        await users.RemoveAuthenticationTokenAsync(ada, "authenticator", "key");

        // Assert
        await Verify(await host.StoredAsync("UserTokens")).DontIgnoreEmptyCollections();
    }

    private static RoleManager<MongoRole> Roles(VaultHost<UsersVault> host) => host.Services.GetRequiredService<RoleManager<MongoRole>>();
}
