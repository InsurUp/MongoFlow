using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>AWS KMS: the master key stays in AWS, which the app asks to encrypt and decrypt the data keys.</summary>
public sealed class AwsKms : KmsProvider
{
    private readonly AwsMasterKey _masterKey;
    private readonly Dictionary<string, object> _credentials;

    /// <param name="masterKey">The customer master key data keys are created under.</param>
    /// <param name="accessKeyId">The access key of an identity allowed to use the master key.</param>
    /// <param name="secretAccessKey">Its secret.</param>
    /// <param name="sessionToken">Its session token, for temporary credentials.</param>
    public AwsKms(AwsMasterKey masterKey,
        string accessKeyId,
        string secretAccessKey,
        string? sessionToken = null)
        : this(masterKey, Credential(accessKeyId, secretAccessKey, sessionToken))
    {
    }

    private AwsKms(AwsMasterKey masterKey,
        Dictionary<string, object> credentials)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        _masterKey = masterKey;
        _credentials = credentials;
    }

    internal override string Name => "aws";

    internal override IReadOnlyDictionary<string, object> Credentials => _credentials;

    internal override BsonDocument MasterKey => _masterKey.ToDocument();

    /// <summary>
    /// AWS KMS with the credentials the environment gives, found as the AWS SDK does: environment variables, an ECS task
    /// role, or the EC2 instance's role.
    /// </summary>
    public static AwsKms FromEnvironment(AwsMasterKey masterKey) => new(masterKey, []);

    private static Dictionary<string, object> Credential(string accessKeyId,
        string secretAccessKey,
        string? sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);

        var credentials = new Dictionary<string, object> { ["accessKeyId"] = accessKeyId, ["secretAccessKey"] = secretAccessKey };
        if (sessionToken is not null)
        {
            credentials["sessionToken"] = sessionToken;
        }

        return credentials;
    }
}
