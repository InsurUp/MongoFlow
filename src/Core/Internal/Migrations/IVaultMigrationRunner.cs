using MongoDB.Driver;
using Semver;

namespace MongoFlow;

/// <summary>Migrates one vault; <see cref="VaultMigrator"/> holds one for each registered vault.</summary>
internal interface IVaultMigrationRunner
{
    Type VaultType { get; }

    /// <summary>The collection the vault records its migrations in, or <see langword="null"/> when it has none.</summary>
    IMongoCollection<MigrationRecord>? History { get; }

    /// <summary>
    /// The client the vault was given: <see cref="History"/>'s is one that encrypts when the vault does, which vaults with
    /// other settings don't share, though they reach the same collections.
    /// </summary>
    IMongoClient Client { get; }

    Task MigrateAsync(SemVersion? target,
        MigrateOptions options,
        CancellationToken cancellationToken);
}
