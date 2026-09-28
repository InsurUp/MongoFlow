namespace MongoFlow;

/// <summary>A collection declared on a vault, as seen while the vault is being configured.</summary>
public interface IVaultCollectionInfo
{
    Type DocumentType { get; }

    /// <summary>The key type declared on the property, or <see langword="null"/> for a keyless collection.</summary>
    Type? KeyType { get; }

    string PropertyName { get; }
}
