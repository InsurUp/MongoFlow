namespace MongoFlow;

/// <summary>A named group of behavior that can be switched off per query or save by its <see cref="Key"/>.</summary>
/// <remarks>
/// <see cref="Configure{TVault}"/> is called once for each vault the feature is added to, and everything it adds
/// through the builder belongs to the feature. Features are created from the root provider once, at startup. The key is
/// static so callers can switch the feature off without an instance: <c>Without(AuditFeature.Key)</c>.
/// </remarks>
public interface IVaultFeature
{
    static abstract FeatureKey Key { get; }

    void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault;
}
