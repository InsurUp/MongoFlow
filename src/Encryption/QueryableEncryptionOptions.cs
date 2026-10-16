using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// Where a vault's data keys are kept, and the key management service (KMS) that protects them, given to
/// <c>UseQueryableEncryption</c>.
/// </summary>
public sealed class QueryableEncryptionOptions
{
    internal CollectionNamespace? KeyVaultNamespace { get; private set; }

    internal KmsProvider? KmsProvider { get; private set; }

    /// <summary>
    /// The collection the data keys are kept in, such as <c>encryption.__keyVault</c>, through the client the vault was
    /// given: one per deployment, usually, shared by every encrypted collection.
    /// </summary>
    public QueryableEncryptionOptions KeyVault(string database,
        string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        KeyVaultNamespace = new CollectionNamespace(database, collection);
        return this;
    }

    /// <summary>
    /// The KMS that protects the data keys, holding or reaching the master key they're created under, such as
    /// <c>new LocalKms(masterKey)</c> or <c>AwsKms.FromEnvironment(new AwsMasterKey(region, keyArn))</c>.
    /// </summary>
    public QueryableEncryptionOptions Kms(KmsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        KmsProvider = provider;
        return this;
    }
}
