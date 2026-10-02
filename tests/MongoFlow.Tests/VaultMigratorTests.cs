using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

/// <summary>
/// What <see cref="IVaultMigrator"/> checks before it reaches the server: the vault is registered, its migrations have a
/// version each, and no two vaults record their migrations in one collection. A vault without migrations has nothing to
/// migrate and no version.
/// </summary>
public partial class VaultMigratorTests
{
    [Test]
    public async Task MigrateAsync_UnregisteredVault_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await ThrowsTask(() => migrator.MigrateAsync<JournalVault>()).IgnoreStackTrace();
    }

    [Test]
    public async Task GetVersionAsync_UnregisteredVault_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await Assert.That(() => migrator.GetVersionAsync<JournalVault>()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task MigrateAsync_TwoMigrationsWithOneVersion_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<JournalVault>(vault => vault.Migrations(m => m.Add<OpenJournal>().Add<ReopenJournal>()));
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await ThrowsTask(() => migrator.MigrateAsync<JournalVault>()).IgnoreStackTrace();
    }

    [Test]
    public async Task MigrateAllAsync_VaultsRecordingInOneCollection_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var provider = VaultsSharingAHistory();
        var migrator = provider.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await ThrowsTask(() => migrator.MigrateAllAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task MigrateAsync_VaultsRecordingInOneCollection_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var provider = VaultsSharingAHistory();
        var migrator = provider.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await Assert.That(() => migrator.MigrateAsync<JournalVault>()).ThrowsExactly<VaultConfigurationException>();
    }

    [Test]
    public async Task MigrateAsync_VaultWithoutMigrations_DoesNothing()
    {
        // Arrange — reaching the server would fail.
        await using var host = new VaultHost<JournalVault>();
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await Assert.That(() => migrator.MigrateAsync<JournalVault>()).ThrowsNothing();
    }

    [Test]
    public async Task GetVersionAsync_VaultWithoutMigrations_ReturnsNull()
    {
        // Arrange
        await using var host = new VaultHost<JournalVault>();
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act
        var version = await migrator.GetVersionAsync<JournalVault>();

        // Assert
        await Assert.That(version).IsNull();
    }

    // Both on the same database, recording in the default collection.
    private static ServiceProvider VaultsSharingAHistory() =>
        new ServiceCollection()
            .AddSingleton(Offline.Client)
            .AddMongoVault<JournalVault>(vault => vault.UseDatabase(Offline.Database).Migrations(m => m.Add<OpenJournal>()))
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(Offline.Database).Migrations(m => m.Add<SeedShop>()))
            .BuildServiceProvider();
}
