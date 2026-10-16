using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// The key management service (KMS) that protects a vault's data keys, given to <c>Kms</c>: <see cref="LocalKms"/>,
/// <see cref="AwsKms"/>, <see cref="AzureKms"/>, <see cref="GcpKms"/> or <see cref="KmipKms"/>. It holds, or reaches, the
/// master key that encrypts the data keys, which never goes into MongoDB.
/// </summary>
public abstract class KmsProvider
{
    private protected KmsProvider()
    {
    }

    /// <summary>
    /// How the driver connects to the KMS, such as a client certificate, which KMIP needs, or a certificate authority of
    /// your own. Certificate validation can't be turned off.
    /// </summary>
    public SslSettings? Tls { get; init; }

    /// <summary>The provider's name, as the driver knows it: <c>local</c>, <c>aws</c>, <c>azure</c>, <c>gcp</c> or <c>kmip</c>.</summary>
    internal abstract string Name { get; }

    /// <summary>
    /// What the driver authenticates with, as it names each setting. Empty means it finds credentials in the environment
    /// when it needs them.
    /// </summary>
    internal abstract IReadOnlyDictionary<string, object> Credentials { get; }

    /// <summary>The master key data keys are created under, or <see langword="null"/> for the local KMS, which is its key.</summary>
    internal abstract BsonDocument? MasterKey { get; }
}
