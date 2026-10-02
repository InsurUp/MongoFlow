namespace MongoFlow;

/// <summary>A collection declared on a vault, as seen while the vault is being configured.</summary>
public interface IVaultCollectionInfo
{
    /// <summary>The type of the collection's documents.</summary>
    Type DocumentType { get; }

    /// <summary>The key type declared on the property, or <see langword="null"/> for a keyless collection.</summary>
    Type? KeyType { get; }

    /// <summary>The vault property declaring the collection, which names it unless its configuration does.</summary>
    string PropertyName { get; }
}
