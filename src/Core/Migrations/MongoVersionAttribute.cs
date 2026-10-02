namespace MongoFlow;

/// <summary>
/// The version a vault's data is migrated to: <see cref="IVaultMigrator.MigrateAllAsync"/>, and
/// <see cref="IVaultMigrator.MigrateAsync{TVault}"/> without a target, apply or revert migrations to reach it rather than
/// the highest. Code can then ship migrations ahead of the data, and going back is a release with a lower version.
/// </summary>
/// <remarks>
/// It must be a semantic version, such as <c>2.0.0</c>, on a vault that declares migrations, which is checked at startup;
/// and the version of one of them, which is checked when the vault is migrated.
/// </remarks>
/// <param name="version">The version of the migration the vault's data is at, such as <c>2.0.0</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MongoVersionAttribute(string version) : Attribute
{
    /// <summary>The version the vault's data is migrated to.</summary>
    public string Version { get; } = version;
}
