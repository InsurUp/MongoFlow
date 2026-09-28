using Semver;

namespace MongoFlow;

public sealed class MigrationFailedException(Type vaultType, SemVersion version, Exception innerException)
    : Exception($"Migration {version} of {vaultType.Name} failed.", innerException)
{
    public Type VaultType { get; } = vaultType;

    public SemVersion Version { get; } = version;
}
