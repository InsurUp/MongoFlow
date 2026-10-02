using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="MongoRoleStore{TVault,TRole,TKey}"/>, through the <see cref="RoleManager{TRole}"/>
/// <c>AddMongoFlowStores</c> registers: a role is one document with its claims inside, found by id or name through the
/// vault's query filters, and what the manager changes on it is saved by its update.
/// </summary>
public class MongoRoleStoreTests
{
    private static readonly ObjectId AdminId = ObjectId.Parse("6600000000000000000000b1");

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task CreateAsync_NewRole_StoresItAsOneDocument()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);

        // Act
        var result = await roles.CreateAsync(Admin());

        // Assert
        await Verify(new { result.Succeeded, Stored = IdentityDocuments.Stable(await host.StoredAsync("Roles")) });
    }

    [Test]
    public async Task FindByIdAndNameAsync_StoredRole_FindIt()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);
        await roles.CreateAsync(Admin());

        // Act
        var byId = await roles.FindByIdAsync(AdminId.ToString());
        var byName = await roles.FindByNameAsync("admin");
        var noId = await roles.FindByIdAsync("");
        var unknown = await roles.FindByNameAsync("owner");

        // Assert
        await Verify(new { ById = byId?.Id, ByName = byName?.Id, NoId = noId?.Id, Unknown = unknown?.Id });
    }

    [Test]
    public async Task UpdateAsync_RenamedRole_ReplacesTheStoredOne()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);
        var admin = Admin();
        await roles.CreateAsync(admin);

        // Act
        await roles.SetRoleNameAsync(admin, "administrator");
        await roles.UpdateAsync(admin);

        // Assert
        await Verify(IdentityDocuments.Stable(await host.StoredAsync("Roles")));
    }

    [Test]
    public async Task DeleteAsync_StoredRole_RemovesIt()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);
        var admin = Admin();
        await roles.CreateAsync(admin);

        // Act
        await roles.DeleteAsync(admin);

        // Assert
        await Verify(await host.StoredAsync("Roles")).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task AddAndRemoveClaimAsync_RoleClaims_AreStoredInsideTheRole()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);
        var admin = Admin();
        await roles.CreateAsync(admin);

        // Act — removing claims the role doesn't have changes nothing.
        await roles.AddClaimAsync(admin, new Claim("permission", "users.read"));
        await roles.AddClaimAsync(admin, new Claim("permission", "users.write"));
        await roles.RemoveClaimAsync(admin, new Claim("permission", "users.write"));
        await roles.RemoveClaimAsync(admin, new Claim("permission", "billing"));
        await roles.RemoveClaimAsync(admin, new Claim("team", "core"));

        // Assert
        await Verify(new
        {
            Claims = (await roles.GetClaimsAsync(admin)).Select(claim => $"{claim.Type}: {claim.Value}"),
            Stored = IdentityDocuments.Stable(await host.StoredAsync("Roles"))
        });
    }

    [Test]
    public async Task Roles_StoredRoles_CanBeQueried()
    {
        // Arrange
        await using var host = Host();
        var roles = Roles(host);
        await roles.CreateAsync(Admin());
        await roles.CreateAsync(new MongoRole { Name = "viewer" });

        // Act
        var names = roles.Roles.Select(x => x.Name!).OrderBy(name => name).ToList();

        // Assert
        await Assert.That(names).IsEquivalentTo(["admin", "viewer"]);
    }

    [Test]
    public async Task GetRoleIdAndNames_StoreMadeWithoutAnErrorDescriber_ReadThemFromTheRole()
    {
        // Arrange
        await using var host = Host();
        var store = new MongoRoleStore<UsersVault, MongoRole, ObjectId>(host.Vault);
        var admin = new MongoRole { Id = AdminId, Name = "admin", NormalizedName = "ADMIN" };

        // Act
        var id = await store.GetRoleIdAsync(admin);
        var unsavedId = await store.GetRoleIdAsync(new MongoRole { Name = "unsaved" });
        var name = await store.GetRoleNameAsync(admin);
        var normalizedName = await store.GetNormalizedRoleNameAsync(admin);

        // Assert
        await Verify(new { id, unsavedId, name, normalizedName, Describer = store.ErrorDescriber.GetType().Name });
    }

    [Test]
    public async Task FindByIdAsync_DisposedStore_ThrowsObjectDisposedException()
    {
        // Arrange
        await using var host = Host();
        var store = host.Services.GetRequiredService<IRoleStore<MongoRole>>();
        store.Dispose();

        // Act & Assert
        await Assert.That(() => store.FindByIdAsync(AdminId.ToString(), CancellationToken.None))
            .ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task Constructor_NullVault_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new MongoRoleStore<UsersVault, MongoRole, ObjectId>(null!)).ThrowsExactly<ArgumentNullException>();
    }

    private static MongoRole Admin() => new() { Id = AdminId, Name = "admin" };

    private static RoleManager<MongoRole> Roles(VaultHost<UsersVault> host) => host.Services.GetRequiredService<RoleManager<MongoRole>>();

    private VaultHost<UsersVault> Host() =>
        Mongo.Host<UsersVault>(services: services => services
            .AddIdentityCore<MongoUser>()
            .AddRoles<MongoRole>()
            .AddMongoFlowStores<UsersVault>());
}
