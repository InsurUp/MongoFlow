using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// A KMIP server, such as HashiCorp Vault or Thales CipherTrust: the master key stays on it. The driver authenticates to it
/// with a client certificate, given as <see cref="KmsProvider.Tls"/>.
/// </summary>
public sealed class KmipKms : KmsProvider
{
    private readonly string _endpoint;
    private readonly KmipMasterKey _masterKey;

    /// <param name="endpoint">The server, as <c>host</c> or <c>host:port</c>; 5696 is KMIP's port.</param>
    /// <param name="tls">The client certificate the server accepts, and its certificate authority if it's your own.</param>
    /// <param name="masterKey">The key data keys are created under; the server creates one without.</param>
    public KmipKms(string endpoint,
        SslSettings tls,
        KmipMasterKey? masterKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(tls);

        _endpoint = endpoint;
        _masterKey = masterKey ?? new KmipMasterKey();
        Tls = tls;
    }

    internal override string Name => "kmip";

    internal override IReadOnlyDictionary<string, object> Credentials => new Dictionary<string, object> { ["endpoint"] = _endpoint };

    internal override BsonDocument MasterKey => _masterKey.ToDocument();
}
