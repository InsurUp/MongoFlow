using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>A key in Azure Key Vault, which <see cref="AzureKms"/> creates data keys under.</summary>
public sealed class AzureMasterKey
{
    /// <param name="keyVaultEndpoint">The vault's host, such as <c>my-vault.vault.azure.net</c>.</param>
    /// <param name="keyName">The key's name in the vault.</param>
    public AzureMasterKey(string keyVaultEndpoint,
        string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVaultEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        KeyVaultEndpoint = keyVaultEndpoint;
        KeyName = keyName;
    }

    /// <summary>The vault's host.</summary>
    public string KeyVaultEndpoint { get; }

    /// <summary>The key's name in the vault.</summary>
    public string KeyName { get; }

    /// <summary>A version of the key; its current one without.</summary>
    public string? KeyVersion { get; init; }

    internal BsonDocument ToDocument() =>
        new BsonDocument { { "keyVaultEndpoint", KeyVaultEndpoint }, { "keyName", KeyName } }.AddIfNotNull("keyVersion", KeyVersion);
}
