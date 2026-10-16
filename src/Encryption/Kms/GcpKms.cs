using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>Google Cloud KMS: the master key stays in Google Cloud, which the app asks to encrypt and decrypt the data keys.</summary>
public sealed class GcpKms : KmsProvider
{
    private readonly GcpMasterKey _masterKey;
    private readonly Dictionary<string, object> _credentials;

    /// <param name="masterKey">The key data keys are created under.</param>
    /// <param name="email">The email of a service account allowed to use the key.</param>
    /// <param name="privateKey">The service account's private key, in base64, as its key file's <c>private_key</c> holds it without the PEM lines.</param>
    /// <param name="endpoint">An OAuth endpoint other than <c>oauth2.googleapis.com</c>.</param>
    public GcpKms(GcpMasterKey masterKey,
        string email,
        string privateKey,
        string? endpoint = null)
        : this(masterKey, Credential(email, privateKey, endpoint))
    {
    }

    private GcpKms(GcpMasterKey masterKey,
        Dictionary<string, object> credentials)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        _masterKey = masterKey;
        _credentials = credentials;
    }

    internal override string Name => "gcp";

    internal override IReadOnlyDictionary<string, object> Credentials => _credentials;

    internal override BsonDocument MasterKey => _masterKey.ToDocument();

    /// <summary>Google Cloud KMS with the service account of the Google Cloud host the app runs on, from its metadata server.</summary>
    public static GcpKms FromEnvironment(GcpMasterKey masterKey) => new(masterKey, []);

    private static Dictionary<string, object> Credential(string email,
        string privateKey,
        string? endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        var credentials = new Dictionary<string, object> { ["email"] = email, ["privateKey"] = privateKey };
        if (endpoint is not null)
        {
            credentials["endpoint"] = endpoint;
        }

        return credentials;
    }
}
