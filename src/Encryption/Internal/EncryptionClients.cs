using MongoDB.Driver;
using MongoDB.Driver.Encryption;

namespace MongoFlow.Encryption;

/// <summary>
/// What encrypts for vaults with the same settings over one client: a client that encrypts, made from that client, and
/// the <see cref="ClientEncryption"/> that encrypts values, which keeps its keys in that client's key vault.
/// </summary>
internal sealed class EncryptionClients : IDisposable
{
    private EncryptionClients(IMongoClient source,
        MongoClient encrypting,
        ClientEncryption clientEncryption)
    {
        Source = source;
        Encrypting = encrypting;
        ClientEncryption = clientEncryption;
    }

    /// <summary>The client the vaults were given, which keeps the key vault, and through which MongoFlow sets up collections.</summary>
    public IMongoClient Source { get; }

    /// <summary>
    /// The client the vaults read and write through: it adds what the server needs to write and query encrypted values,
    /// and decrypts what it reads. It analyzes nothing: MongoFlow encrypts the values itself, as MongoDB Community needs.
    /// </summary>
    public MongoClient Encrypting { get; }

    public ClientEncryption ClientEncryption { get; }

    public static EncryptionClients Create(IMongoClient source,
        CollectionNamespace keyVault,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> kmsProviders)
    {
        RegisterAutoEncryption();

        var settings = source.Settings.Clone();

        // From these settings, without AutoEncryptionOptions, the driver makes a client of its own that reads collections'
        // schemas, and disposes that client's cluster when the client that encrypts is disposed. With the given client's
        // settings it would be the given client's cluster: a configurator of its own, which configures the same, keeps it
        // apart.
        var configure = settings.ClusterConfigurator;
        settings.ClusterConfigurator = builder => configure?.Invoke(builder);

        settings.AutoEncryptionOptions = new AutoEncryptionOptions(keyVault,
            kmsProviders,
            keyVaultClient: new Optional<IMongoClient>(source),
            bypassQueryAnalysis: true);

        var clientEncryption = new ClientEncryption(new ClientEncryptionOptions(source, keyVault, kmsProviders));

        return new EncryptionClients(source, new MongoClient(settings), clientEncryption);
    }

    public void Dispose()
    {
        ClientEncryption.Dispose();
        Encrypting.Dispose();
    }

    // The driver has one provider of auto encryption per process, which refuses a second registration: by the app, or for
    // an earlier client.
    private static void RegisterAutoEncryption()
    {
        try
        {
            MongoClientSettings.Extensions.AddAutoEncryption();
        }
        catch (MongoConfigurationException)
        {
            // Registered already.
        }
    }
}
