using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
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

    [Test]
    public async Task MigrateAllAsync_PendingBelowAndAppliedAboveTheVersion_RevertsBeforeApplying()
    {
        // Arrange — a release applied 1.0.0 and 2.0.0; the next one adds 1.1.0 and is pinned to it.
        var database = Mongo.NewDatabase();
        await using (var before = Pinned<PinnedVault>(m => m.Add<PinnedFirst>().Add<PinnedThird>(), database))
        {
            await before.Services.GetRequiredService<IVaultMigrator>().MigrateAsync<PinnedVault>(new SemVersion(2, 0, 0));
        }

        _steps.Entries.Clear();
        await using var host = Pinned<PinnedVault>(m => m.Add<PinnedFirst>().Add<PinnedSecond>().Add<PinnedThird>(), database);

        // Act
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

        // Assert
        await Verify(_steps.Entries);
    }

    private VaultHost<TVault> Pinned<TVault>(Action<IMigrationBuilder<TVault>> migrations,
        IMongoDatabase? database = null) where TVault : MongoVault =>
        new(Mongo.Client, database ?? Mongo.NewDatabase(), vault => vault.Migrations(migrations), services => Register(services));
}
