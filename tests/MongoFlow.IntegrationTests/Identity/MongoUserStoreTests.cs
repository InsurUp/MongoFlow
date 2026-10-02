using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="MongoUserStore{TVault,TUser,TRole,TKey}"/>, through the <see cref="UserManager{TUser}"/>
/// <c>AddMongoFlowStores</c> registers:
/// <list type="number">
/// <item>a user is one document, with its claims, logins, role ids and passkeys inside, given an id on insert;</item>
/// <item>users are found by id, name, email, claim, login, role and passkey, through the vault's query filters, also
/// asynchronous ones for the synchronous <c>Users</c>;</item>
/// <item>what the manager changes on a user is saved once, by its update;</item>
/// <item>tokens are documents of their own, given a key with string keys too, added, changed and removed with the
/// user's save, and deleted with the user;</item>
/// <item>an update or delete applies only while the stored concurrency stamp is the one read, and while no other
/// transaction is changing the user, failing with Identity's concurrency failure otherwise, and an update renews it; see
/// <c>MongoUserStoreTests.Concurrency.cs</c>.</item>
/// </list>
/// </summary>
public partial class MongoUserStoreTests
{
    private static readonly ObjectId AdaId = ObjectId.Parse("6600000000000000000000a1");

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task CreateAsync_NewUser_StoresItAsOneDocument()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);

        // Act
        var result = await users.CreateAsync(Ada());

        // Assert
        await Verify(new { result.Succeeded, Stored = IdentityDocuments.Stable(await host.StoredAsync("Users")) });
    }

    [Test]
    public async Task CreateAsync_UserWithoutAnId_GetsOneOnInsert()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var user = new MongoUser { UserName = "bob" };

        // Act
        await users.CreateAsync(user);

        // Assert
        await Assert.That((await users.FindByIdAsync(user.Id.ToString()))?.UserName).IsEqualTo("bob");
    }

    [Test]
    public async Task FindByIdNameAndEmail_StoredUser_FindIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        await users.CreateAsync(Ada());

        // Act
        var byId = await users.FindByIdAsync(AdaId.ToString());
        var byName = await users.FindByNameAsync("ADA");
        var byEmail = await users.FindByEmailAsync("ada@example.com");
        var unknown = await users.FindByNameAsync("nobody");
        var notAnId = await users.FindByIdAsync("not-an-id");

        // Assert
        await Verify(new { ById = byId?.Id, ByName = byName?.Id, ByEmail = byEmail?.Id, Unknown = unknown?.Id, NotAnId = notAnId?.Id });
    }

    [Test]
    public async Task UpdateAsync_ChangedUser_ReplacesTheStoredOne()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.SetPhoneNumberAsync(ada, "+90 555 000 00 00");

        // Assert
        await Verify(IdentityDocuments.Stable(await host.StoredAsync("Users")));
    }

    [Test]
    public async Task DeleteAsync_StoredUser_RemovesIt()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);

        // Act
        await users.DeleteAsync(ada);

        // Assert
        await Verify(await host.StoredAsync("Users")).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Users_StoredUsers_CanBeQueried()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        await users.CreateAsync(Ada());
        await users.CreateAsync(new MongoUser { UserName = "bob" });

        // Act
        var names = users.Users.Where(x => x.UserName != "bob").Select(x => x.UserName!).ToList();

        // Assert
        await Assert.That(names).IsEquivalentTo(["ada"]);
    }

    [Test]
    public async Task Users_AsynchronousQueryFilter_IsWaitedFor()
    {
        // Arrange
        await using var host = Host(vault => vault.QueryFilter<MongoUser>(HideBobAsync));
        var users = Users(host);
        await users.CreateAsync(Ada());
        await users.CreateAsync(new MongoUser { UserName = "bob" });

        // Act
        var names = users.Users.Select(x => x.UserName!).ToList();

        // Assert
        await Assert.That(names).IsEquivalentTo(["ada"]);
    }

    [Test]
    public async Task FindByIdAsync_DisposedStore_ThrowsObjectDisposedException()
    {
        // Arrange
        await using var host = Host();
        var store = host.Services.GetRequiredService<IUserStore<MongoUser>>();
        store.Dispose();

        // Act & Assert
        await Assert.That(() => store.FindByIdAsync(AdaId.ToString(), CancellationToken.None)).ThrowsExactly<ObjectDisposedException>();
    }

    private static MongoUser Ada() => new() { Id = AdaId, UserName = "ada", Email = "ada@example.com" };

    private static UserManager<MongoUser> Users(VaultHost<UsersVault> host) => host.Services.GetRequiredService<UserManager<MongoUser>>();

    // Resolved after a yield, so the query filter isn't ready at once.
    private static async ValueTask<Expression<Func<MongoUser, bool>>> HideBobAsync(IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        return x => x.UserName != "bob";
    }

    private VaultHost<UsersVault> Host(Action<IVaultBuilder<UsersVault>>? configure = null) =>
        Mongo.Host(configure, services => services.AddIdentityCore<MongoUser>().AddRoles<MongoRole>().AddMongoFlowStores<UsersVault>());
}
