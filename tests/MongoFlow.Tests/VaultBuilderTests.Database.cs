using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    public async Task UseDatabase_Name_UsesTheRegisteredClient()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var database = host.Vault.Orders.MongoCollection.Database;

        // Assert
        await Assert.That(database.Client).IsSameReferenceAs(Offline.Client);
        await Assert.That(database.DatabaseNamespace.DatabaseName).IsEqualTo(Offline.DatabaseName);
    }

    [Test]
    public async Task UseDatabase_Database_UsesIt()
    {
        // Arrange
        var database = Offline.Client.GetDatabase("other");
        await using var host = new VaultHost<ShopVault>(vault => vault.UseDatabase(database));

        // Act
        var used = host.Vault.Orders.MongoCollection.Database;

        // Assert
        await Assert.That(used).IsSameReferenceAs(database);
    }

    [Test]
    public async Task UseDatabase_InADefaultAndTheVaultsOwn_UsesTheOwn()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(services: services =>
            services.AddDefaultVaultConfiguration(typeof(DatabaseDefault<>)));

        // Act
        var name = host.Vault.Orders.MongoCollection.Database.DatabaseNamespace.DatabaseName;

        // Assert
        await Assert.That(name).IsEqualTo(Offline.DatabaseName);
    }

    [Test]
    public async Task UseDatabase_OnlyInADefault_UsesTheDefaults()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddSingleton(Offline.Client)
            .AddDefaultVaultConfiguration(typeof(DatabaseDefault<>))
            .AddMongoVault<ShopVault>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act
        var name = scope.ServiceProvider.GetRequiredService<ShopVault>().Orders.MongoCollection.Database.DatabaseNamespace.DatabaseName;

        // Assert
        await Assert.That(name).IsEqualTo(DatabaseDefault<ShopVault>.Name);
    }

    [Test]
    public async Task UseDatabase_NameWithoutARegisteredClient_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(Offline.DatabaseName))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act & Assert
        await Assert.That(() => scope.ServiceProvider.GetRequiredService<ShopVault>()).ThrowsExactly<InvalidOperationException>();
    }
}
