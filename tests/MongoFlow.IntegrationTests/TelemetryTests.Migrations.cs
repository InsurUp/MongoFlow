using Microsoft.Extensions.DependencyInjection;
using Semver;

namespace MongoFlow.IntegrationTests;

public partial class TelemetryTests
{
    [Test]
    public async Task MigrateAsync_ApplyingAndReverting_TracesEachMigration()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>(vault => vault.Migrations(m => m.Add<AddOrder>()));
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act
        await migrator.MigrateAsync<ShopVault>();
        await migrator.MigrateAsync<ShopVault>(new SemVersion(0, 0, 0));

        // Assert
        await Verify(spans.Snapshot());
    }

    [Test]
    public async Task MigrateAsync_AMigrationFails_TracesTheFailure()
    {
        // Arrange
        using var spans = new ActivitySink();
        await using var host = Mongo.Host<ShopVault>(vault => vault.Migrations(m => m.Add<AddOrder>().Add<FailToApply>()));
        var migrator = host.Services.GetRequiredService<IVaultMigrator>();

        // Act
        await Assert.That(() => migrator.MigrateAsync<ShopVault>()).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(spans.Snapshot());
    }
}
