using Semver;

namespace MongoFlow;

/// <summary>A migration failed. What it did in its transaction was rolled back, and it wasn't recorded.</summary>
public sealed class MigrationFailedException : Exception
{
    internal MigrationFailedException(Type vaultType,
        SemVersion version,
        bool up,
        Exception innerException)
        : base($"{(up ? "Applying" : "Reverting")} migration {version} of {vaultType.Name} failed.", innerException)
    {
        VaultType = vaultType;
        Version = version;
    }

    public Type VaultType { get; }

    public SemVersion Version { get; }
}
