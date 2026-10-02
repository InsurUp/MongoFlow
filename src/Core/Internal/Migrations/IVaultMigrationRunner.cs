using MongoDB.Driver;
using Semver;

namespace MongoFlow;

/// <summary>Migrates one vault; <see cref="VaultMigrator"/> holds one for each registered vault.</summary>
internal interface IVaultMigrationRunner
{
    Type VaultType { get; }

    /// <summary>The collection the vault records its migrations in, or <see langword="null"/> when it has none.</summary>
    IMongoCollection<MigrationRecord>? History { get; }

    Task MigrateAsync(SemVersion? target,
        CancellationToken cancellationToken);
}
