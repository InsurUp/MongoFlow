using MongoDB.Bson;
using Semver;

namespace MongoFlow.IntegrationTests;

public partial class MigrationTests
{
    [Test]
    public async Task MigrateAsync_MigrationFailsAfterSaving_RollsBackItsWritesAndThrowsMigrationFailedException()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<FailAfterSaving>());

        // Act
        var exception = await Assert.That(() => Migrator(host).MigrateAsync<RegistryVault>()).ThrowsExactly<MigrationFailedException>();

        // Assert — the migration before it stays applied.
        await Verify(new
        {
            exception!.Message,
            Version = exception.Version.ToString(),
            Vault = exception.VaultType.Name,
            Cause = exception.InnerException!.Message,
            Stored = await host.StoredAsync("Entries"),
            History = await HistoryAsync(host),
            Log = _sink.Snapshot(MongoFlowLogEvents.Migrations.Category)
        });
    }

    [Test]
    public async Task MigrateAsync_MigrationLeavesWritesUnsaved_ThrowsMigrationFailedExceptionWithoutRecordingIt()
    {
        // Arrange
        await using var host = Host(m => m.Add<ForgetToSave>());

        // Act
        var exception = await Assert.That(() => Migrator(host).MigrateAsync<RegistryVault>()).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new { Cause = exception!.InnerException!.Message, Stored = await host.StoredAsync("Entries"), History = await HistoryAsync(host) })
            .DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task MigrateAsync_MigrationLeavesTrackedChangesUnsaved_ThrowsMigrationFailedExceptionWithoutRecordingIt()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<ForgetTrackedChange>());

        // Act
        await Assert.That(() => Migrator(host).MigrateAsync<RegistryVault>()).ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Entries"), History = await HistoryAsync(host) });
    }

    [Test]
    public async Task MigrateAsync_MigrationSavingWhatItTracked_IsAppliedAndRecorded()
    {
        // Arrange — one tracked entry deleted in the migration's transaction, the other unchanged: nothing is unsaved.
        await using var host = Host(m => m.Add<SeedEntries>().Add<DeleteTrackedEntry>());

        // Act
        await Migrator(host).MigrateAsync<RegistryVault>();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Entries"), Versions = (await HistoryAsync(host)).Count });
    }

    [Test]
    public async Task MigrateAsync_RevertingFails_ThrowsMigrationFailedExceptionAndKeepsItRecorded()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<Irreversible>());
        var migrator = Migrator(host);
        await migrator.MigrateAsync<RegistryVault>();

        // Act
        var exception = await Assert.That(() => migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 0, 0)))
            .ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new { exception!.Message, Cause = exception.InnerException!.Message, History = await HistoryAsync(host) });
    }

    [Test]
    public async Task MigrateAsync_RevertingAVersionRecordedTwice_RevertsItOnce()
    {
        // Arrange — two instances migrating at once under an earlier version recorded 1.1.0 twice.
        await using var host = Host(m => m.Add<SeedEntries>().Add<ActivateEntries>());
        await host.SeedAsync("migrations",
            LegacyRecord(ObjectId.Parse("5f0c0ffee000000000001234")),
            LegacyRecord(ObjectId.Parse("5f0c0ffee000000000001235"), "1.1.0", "ActivateEntries"),
            LegacyRecord(ObjectId.Parse("5f0c0ffee000000000001236"), "1.1.0", "ActivateEntries"));
        var migrator = Migrator(host);

        // Act
        await migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 0, 0));
        await migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 0, 0));

        // Assert
        await Verify(new { _steps.Entries, History = await HistoryAsync(host) });
    }

    [Test]
    public async Task MigrateAsync_RevertedByAnotherInstanceMeanwhile_ThrowsMigrationFailedExceptionAndRollsBack()
    {
        // Arrange
        await using var host = Host(m => m.Add<SeedEntries>().Add<RevertedElsewhere>());
        var migrator = Migrator(host);
        await migrator.MigrateAsync<RegistryVault>();

        // Act
        var exception = await Assert.That(() => migrator.MigrateAsync<RegistryVault>(new SemVersion(1, 0, 0)))
            .ThrowsExactly<MigrationFailedException>();

        // Assert
        await Verify(new { exception!.Message, Cause = exception.InnerException!.Message, History = await HistoryAsync(host) });
    }
}
