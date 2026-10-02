using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="UserManagerExtensions.Without{TUser}"/> and <see cref="RoleManagerExtensions.Without{TRole}"/>: the managers
/// <c>AddMongoFlowStores</c> registers give copies whose reads and writes have a vault feature switched off; any other
/// manager can't.
/// </summary>
public partial class ManagersWithoutTests
{
    private static readonly ObjectId AdaId = ObjectId.Parse("6600000000000000000000a1");
    private static readonly ObjectId AdminId = ObjectId.Parse("6600000000000000000000b1");

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task UserManagerWithout_SoftDeleteOff_FindsAndDeletesDeletedUsers()
    {
        // Arrange
        await using var host = Host();
        var users = host.Services.GetRequiredService<UserManager<ArchivableUser>>();
        var ada = new ArchivableUser { Id = AdaId, UserName = "ada" };
        await users.CreateAsync(ada);
        await users.DeleteAsync(ada);
        var withoutSoftDelete = users.Without(SoftDeleteFeature.Key);

        // Act
        var found = await users.FindByIdAsync(AdaId.ToString());
        var foundWithout = await withoutSoftDelete.FindByIdAsync(AdaId.ToString());
        await withoutSoftDelete.DeleteAsync(foundWithout!);

        // Assert
        await Verify(new { Found = found?.UserName, FoundWithout = foundWithout?.UserName, Stored = await host.StoredAsync("Users") })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task RoleManagerWithout_SoftDeleteOff_FindsDeletedRoles()
    {
        // Arrange
        await using var host = Host();
        var roles = host.Services.GetRequiredService<RoleManager<ArchivableRole>>();
        var admin = new ArchivableRole { Id = AdminId, Name = "admin" };
        await roles.CreateAsync(admin);
        await roles.DeleteAsync(admin);

        // Act
        var found = await roles.FindByNameAsync("admin");
        var foundWithout = await roles.Without(SoftDeleteFeature.Key).FindByNameAsync("admin");

        // Assert
        await Verify(new { Found = found?.Name, FoundWithout = new { foundWithout?.Name, foundWithout?.IsDeleted } });
    }

    [Test]
    public async Task UserManagerWithout_ManagerNotFromAddMongoFlowStores_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Host();
        var users = new UserManager<ArchivableUser>(host.Services.GetRequiredService<IUserStore<ArchivableUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);

        // Act & Assert
        await Throws(() => users.Without(SoftDeleteFeature.Key)).IgnoreStackTrace();
    }

    [Test]
    public async Task RoleManagerWithout_ManagerNotFromAddMongoFlowStores_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Host();
        var roles = new RoleManager<ArchivableRole>(host.Services.GetRequiredService<IRoleStore<ArchivableRole>>(),
            [], null!, null!, null!);

        // Act & Assert
        await Throws(() => roles.Without(SoftDeleteFeature.Key)).IgnoreStackTrace();
    }

    [Test]
    public async Task UserAndRoleManagerWithout_NullManager_ThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => ((UserManager<ArchivableUser>)null!).Without(SoftDeleteFeature.Key)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => ((RoleManager<ArchivableRole>)null!).Without(SoftDeleteFeature.Key)).ThrowsExactly<ArgumentNullException>();
    }

    private VaultHost<ArchiveVault> Host() =>
        Mongo.Host<ArchiveVault>(
            vault => vault.UseSoftDelete((IArchivable x) => x.IsDeleted),
            services => services.AddIdentityCore<ArchivableUser>().AddRoles<ArchivableRole>().AddMongoFlowStores<ArchiveVault>());
}
