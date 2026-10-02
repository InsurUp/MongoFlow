using Microsoft.Extensions.DependencyInjection;
using Semver;

namespace MongoFlow.IntegrationTests;

public partial class MigrationTests
{
    [Test]
    public async Task MigrateAllAsync_VaultWithAMongoVersion_MigratesToIt()
    {
        // Arrange
        await using var host = Pinned<PinnedVault>(m => m.Add<PinnedFirst>().Add<PinnedSecond>().Add<PinnedThird>());
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act
        await migrator.MigrateAllAsync();

        // Assert
        await Verify(new { _steps.Entries, Version = (await migrator.GetVersionAsync<PinnedVault>())?.ToString() });
    }

    [Test]
    public async Task MigrateAllAsync_MongoVersionBelowTheAppliedVersions_RevertsDownToIt()
    {
        // Arrange — a target given to MigrateAsync beats the attribute, so the highest is applied first.
        await using var host = Pinned<PinnedVault>(m => m.Add<PinnedFirst>().Add<PinnedSecond>().Add<PinnedThird>());
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();
        await migrator.MigrateAsync<PinnedVault>(new SemVersion(2, 0, 0));

        // Act
        await migrator.MigrateAllAsync();

        // Assert
        await Verify(new { _steps.Entries, Version = (await migrator.GetVersionAsync<PinnedVault>())?.ToString() });
    }

    [Test]
    public async Task MigrateAsync_MongoVersionNoMigrationHas_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = Pinned<MistypedVault>(m => m.Add<MistypedFirst>().Add<MistypedSecond>());
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act & Assert
        await ThrowsTask(() => migrator.MigrateAsync<MistypedVault>()).IgnoreStackTrace();
    }

    private VaultHost<TVault> Pinned<TVault>(Action<IMigrationBuilder<TVault>> migrations) where TVault : MongoVault =>
        new(Mongo.Client, Mongo.NewDatabase(), vault => vault.Migrations(migrations), services => Register(services));
}
