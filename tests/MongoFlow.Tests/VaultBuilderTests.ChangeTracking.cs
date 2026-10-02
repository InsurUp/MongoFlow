using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    public async Task UseChangeTracking_NotCalled_LeavesItOff()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var tracks = host.Vault.Runtime.Model.TracksChanges;

        // Assert
        await Assert.That(tracks).IsFalse();
    }

    [Test]
    public async Task UseChangeTracking_InADefault_SwitchesItOn()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(services: services =>
            services.AddDefaultVaultConfiguration(typeof(TrackingDefault<>)));

        // Act
        var tracks = host.Vault.Runtime.Model.TracksChanges;

        // Assert
        await Assert.That(tracks).IsTrue();
    }

    [Test]
    public async Task UseChangeTracking_OnInADefaultOffInTheVaultsOwn_LeavesItOff()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.UseChangeTracking(false),
            services => services.AddDefaultVaultConfiguration(typeof(TrackingDefault<>)));

        // Act
        var tracks = host.Vault.Runtime.Model.TracksChanges;

        // Assert
        await Assert.That(tracks).IsFalse();
    }
}
