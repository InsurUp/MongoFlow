using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="MongoFlowIdentityBuilderExtensions.AddMongoFlowStores{TVault}"/> accepts only an Identity vault whose user
/// and role types are Identity's, after <c>AddRoles</c>, and says what's wrong otherwise.
/// </summary>
public partial class MongoFlowIdentityBuilderExtensionsTests
{
    [Test]
    public async Task AddMongoFlowStores_VaultThatIsNotAnIdentityVault_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddIdentityCore<MongoUser>().AddRoles<MongoRole>().AddMongoFlowStores<ShopVault>())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task AddMongoFlowStores_UserThatIsNotAMongoUser_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddIdentityCore<PlainUser>().AddRoles<MongoRole>().AddMongoFlowStores<UsersVault>())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task AddMongoFlowStores_BeforeAddRoles_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddIdentityCore<MongoUser>().AddMongoFlowStores<UsersVault>()).IgnoreStackTrace();
    }

    [Test]
    public async Task AddMongoFlowStores_RoleThatIsNotAMongoRole_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddIdentityCore<MongoUser>().AddRoles<PlainRole>().AddMongoFlowStores<UsersVault>())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task AddMongoFlowStores_UserTypeOtherThanTheVaults_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddIdentityCore<OtherUser>().AddRoles<MongoRole>().AddMongoFlowStores<UsersVault>())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task AddMongoFlowStores_NullBuilder_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => ((IdentityBuilder)null!).AddMongoFlowStores<UsersVault>()).ThrowsExactly<ArgumentNullException>();
    }
}
