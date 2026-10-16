using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// Where a vault's data keys are kept, and the key management service (KMS) that protects them, given to
/// <c>UseQueryableEncryption</c>.
/// </summary>
public sealed class QueryableEncryptionOptions
{
    internal CollectionNamespace? KeyVaultNamespace { get; private set; }

    internal string? KmsProvider { get; private set; }

    internal IReadOnlyDictionary<string, object>? KmsCredentials { get; private set; }

    internal BsonDocument? MasterKey { get; private set; }

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
    /// The KMS that encrypts the data keys, as the driver names it: <c>local</c>, with a 96-byte <c>key</c>, or
    /// <c>aws</c>, <c>azure</c>, <c>gcp</c> or <c>kmip</c>, with their credentials. Data keys are created with it, under
    /// <paramref name="masterKey"/>, the customer master key it holds, which every provider but <c>local</c> needs.
    /// </summary>
    /// <param name="provider">The KMS provider's name, such as <c>local</c> or <c>aws</c>.</param>
    /// <param name="credentials">Its credentials, such as <c>{ ["key"] = masterKeyBytes }</c> for <c>local</c>.</param>
    /// <param name="masterKey">
    /// The customer master key, such as <c>{ region: "eu-central-1", key: "arn:aws:kms:..." }</c> for AWS.
    /// </param>
    public QueryableEncryptionOptions Kms(string provider,
        IReadOnlyDictionary<string, object> credentials,
        BsonDocument? masterKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(credentials);

        KmsProvider = provider;
        KmsCredentials = credentials;
        MasterKey = masterKey;
        return this;
    }
}
