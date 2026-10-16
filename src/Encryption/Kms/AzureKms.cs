using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>Azure Key Vault: the master key stays in the vault, which the app asks to wrap and unwrap the data keys.</summary>
public sealed class AzureKms : KmsProvider
{
    private readonly AzureMasterKey _masterKey;
    private readonly Dictionary<string, object> _credentials;

    /// <param name="masterKey">The key data keys are created under.</param>
    /// <param name="tenantId">The Microsoft Entra tenant of the app registration.</param>
    /// <param name="clientId">The app registration's client id, allowed to use the key.</param>
    /// <param name="clientSecret">Its client secret.</param>
    /// <param name="identityPlatformEndpoint">A sign-in endpoint other than <c>login.microsoftonline.com</c>, for a sovereign cloud.</param>
    public AzureKms(AzureMasterKey masterKey,
        string tenantId,
        string clientId,
        string clientSecret,
        string? identityPlatformEndpoint = null)
        : this(masterKey, Credential(tenantId, clientId, clientSecret, identityPlatformEndpoint))
    {
    }

    private AzureKms(AzureMasterKey masterKey,
        Dictionary<string, object> credentials)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        _masterKey = masterKey;
        _credentials = credentials;
    }

    internal override string Name => "azure";

    internal override IReadOnlyDictionary<string, object> Credentials => _credentials;

    internal override BsonDocument MasterKey => _masterKey.ToDocument();

    /// <summary>Azure Key Vault with the managed identity of the Azure host the app runs on.</summary>
    public static AzureKms FromManagedIdentity(AzureMasterKey masterKey) => new(masterKey, []);

    private static Dictionary<string, object> Credential(string tenantId,
        string clientId,
        string clientSecret,
        string? identityPlatformEndpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);

        var credentials = new Dictionary<string, object>
        {
            ["tenantId"] = tenantId,
            ["clientId"] = clientId,
            ["clientSecret"] = clientSecret
        };

        if (identityPlatformEndpoint is not null)
        {
            credentials["identityPlatformEndpoint"] = identityPlatformEndpoint;
        }

        return credentials;
    }
}
