using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Claims, logins and passkeys, kept inside the user.
public partial class MongoUserStoreTests
{
    [Test]
    public async Task AddReplaceAndRemoveClaims_UserClaims_AreStoredInsideTheUser()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.AddClaimsAsync(ada, [new Claim("team", "core"), new Claim("level", "1")]);
        await users.ReplaceClaimAsync(ada, new Claim("level", "1"), new Claim("level", "2"));
        await users.RemoveClaimsAsync(ada, [new Claim("team", "core")]);

        // Assert
        await Verify(new
        {
            Claims = (await users.GetClaimsAsync(ada)).Select(claim => $"{claim.Type}: {claim.Value}"),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("Users"))
        });
    }

    [Test]
    public async Task GetUsersForClaimAsync_ClaimOfOneUser_FindsThatUser()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await users.CreateAsync(new MongoUser { UserName = "bob" });
        await users.AddClaimAsync(ada, new Claim("team", "core"));

        // Act
        var found = await users.GetUsersForClaimAsync(new Claim("team", "core"));

        // Assert
        await Assert.That(found.Select(user => user.UserName!)).IsEquivalentTo(["ada"]);
    }

    [Test]
    public async Task AddLoginAsync_ThenFindByLoginAsync_FindsTheUser()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.AddLoginAsync(ada, new UserLoginInfo("github", "ada-gh", "GitHub"));

        // Assert
        await Verify(new
        {
            Found = (await users.FindByLoginAsync("github", "ada-gh"))?.UserName,
            Logins = (await users.GetLoginsAsync(ada)).Select(login => $"{login.LoginProvider}: {login.ProviderKey}"),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("Users"))
        });
    }

    [Test]
    public async Task RemoveLoginAsync_StoredLogin_RemovesIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await users.AddLoginAsync(ada, new UserLoginInfo("github", "ada-gh", "GitHub"));

        // Act — removing a login the user doesn't have changes nothing.
        await users.RemoveLoginAsync(ada, "google", "ada-g");
        await users.RemoveLoginAsync(ada, "github", "ada-gh");

        // Assert
        await Assert.That(await users.FindByLoginAsync("github", "ada-gh")).IsNull();
    }

    [Test]
    public async Task AddOrUpdatePasskeyAsync_NewAndThenChanged_KeepsOnePasskeyPerCredential()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.AddOrUpdatePasskeyAsync(ada, Passkey(1, "laptop", signCount: 1));
        await users.AddOrUpdatePasskeyAsync(ada, Passkey(2, "phone", signCount: 1));
        await users.AddOrUpdatePasskeyAsync(ada, Passkey(1, "laptop", signCount: 5));

        // Assert
        await Verify(new
        {
            Passkeys = (await users.GetPasskeysAsync(ada)).Select(passkey => new { passkey.Name, passkey.SignCount }),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("Users"))
        });
    }

    [Test]
    public async Task FindByPasskeyIdAndGetPasskeyAsync_StoredPasskey_FindIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await users.AddOrUpdatePasskeyAsync(ada, Passkey(1, "laptop", signCount: 1));

        // Act
        var byCredential = await users.FindByPasskeyIdAsync([1, 1, 1]);
        var passkey = await users.GetPasskeyAsync(ada, [1, 1, 1]);
        var unknown = await users.GetPasskeyAsync(ada, [9, 9, 9]);

        // Assert
        await Verify(new { User = byCredential?.UserName, Passkey = passkey?.Name, Unknown = unknown?.Name });
    }

    [Test]
    public async Task RemovePasskeyAsync_StoredPasskey_RemovesIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        await users.AddOrUpdatePasskeyAsync(ada, Passkey(1, "laptop", signCount: 1));

        // Act
        await users.RemovePasskeyAsync(ada, [1, 1, 1]);

        // Assert
        await Assert.That(await users.FindByPasskeyIdAsync([1, 1, 1])).IsNull();
    }

    // A passkey whose every byte array is filled with the credential's byte.
    private static UserPasskeyInfo Passkey(byte credential,
        string name,
        uint signCount) =>
        new([credential, credential, credential],
            [credential],
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            signCount,
            ["internal"],
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            [credential],
            [credential])
        {
            Name = name
        };
}
