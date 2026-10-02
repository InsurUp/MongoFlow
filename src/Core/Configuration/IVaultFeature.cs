namespace MongoFlow;

/// <summary>A named group of behavior that can be switched off per query or save by its <see cref="Key"/>.</summary>
/// <remarks>
/// <see cref="Configure{TVault}"/> is called once for each vault the feature is added to, and everything it adds
/// through the builder belongs to the feature. Features are created from the root provider once, when the vault is
/// configured. The key is static so callers can switch the feature off without an instance:
/// <c>Without(AuditFeature.Key)</c>.
/// </remarks>
public interface IVaultFeature
{
    /// <summary>The key that switches the feature off.</summary>
    static abstract FeatureKey Key { get; }

    /// <summary>Adds what the feature does to a vault, such as query filters and interceptors.</summary>
    void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault;
}
