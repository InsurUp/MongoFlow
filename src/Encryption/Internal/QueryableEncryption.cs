using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>A vault's Queryable Encryption, as <c>UseQueryableEncryption</c> configured it.</summary>
internal sealed class QueryableEncryption : VaultEncryption
{
    private readonly CollectionNamespace _keyVault;
    private readonly KmsProvider _kms;
    private readonly SharedVaultResources _shared;
    private EncryptionClients? _clients;

    public QueryableEncryption(QueryableEncryptionOptions options,
        IServiceProvider services)
    {
        _keyVault = options.KeyVaultNamespace ?? throw new VaultConfigurationException(
            "UseQueryableEncryption needs the collection the data keys are kept in: call KeyVault, such as " +
            "KeyVault(\"encryption\", \"__keyVault\").");
        _kms = options.KmsProvider ?? throw new VaultConfigurationException(
            "UseQueryableEncryption needs the KMS that protects the data keys: call Kms, such as Kms(new LocalKms(masterKey)).");
        _shared = services.GetRequiredService<SharedVaultResources>();
    }

    public override IMongoDatabase Database(IMongoDatabase source)
    {
        if (source.Client.Settings.AutoEncryptionOptions is not null)
        {
            throw new VaultConfigurationException(
                "A vault that encrypts with UseQueryableEncryption needs a client without AutoEncryptionOptions of its own: " +
                "MongoFlow makes the client that encrypts from it.");
        }

        _clients = _shared.GetOrAdd(new ClientsKey(source.Client, _keyVault, _kms.Name, Fingerprint(), _kms.Tls),
            () => EncryptionClients.Create(source.Client, _keyVault, _kms));

        return _clients.Encrypting.GetDatabase(source.DatabaseNamespace.DatabaseName, source.Settings);
    }

    public override ICollectionEncryption ForCollection<TDocument>(CollectionModel<TDocument> collection,
        IReadOnlyList<EncryptedMember> members)
    {
        var name = collection.Namespace.CollectionName;
        var fields = members.Select(member => FieldSchema.Of(member, name)).ToList();

        return new CollectionEncryption(collection.Namespace, fields, _clients!);
    }

    public override async Task PrepareAsync(VaultModel model,
        CancellationToken cancellationToken)
    {
        var collections = model.Collections.Where(collection => collection.Encryption is CollectionEncryption).ToList();
        if (collections.Count == 0)
        {
            return;
        }

        await EnsureKeyVaultIndexAsync(cancellationToken);

        foreach (var collection in collections)
        {
            await SetUpAsync(model, collection, cancellationToken);
        }
    }

    public override async Task PrepareCollectionAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken)
    {
        await EnsureKeyVaultIndexAsync(cancellationToken);
        await SetUpAsync(model, collection, cancellationToken);
    }

    private Task SetUpAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken) =>
        new CollectionSetup((CollectionEncryption)collection.Encryption!,
                collection.Settings,
                _clients!,
                model.SourceDatabase,
                model.Logs.Encryption,
                _kms.Name,
                _kms.MasterKey)
            .RunAsync(cancellationToken);

    /// <summary>
    /// The unique index on the data keys' alternate names MongoDB recommends for a key vault, so two keys can't share a
    /// name.
    /// </summary>
    private async Task EnsureKeyVaultIndexAsync(CancellationToken cancellationToken)
    {
        var keyVault = _clients!.Source.GetDatabase(_keyVault.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(_keyVault.CollectionName);

        await keyVault.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(new BsonDocument("keyAltNames", 1),
                new CreateIndexOptions<BsonDocument>
                {
                    Unique = true,
                    PartialFilterExpression = new BsonDocument("keyAltNames", new BsonDocument("$exists", true))
                }),
            cancellationToken: cancellationToken);
    }

    // Vaults over one client share what encrypts only with the same key vault and KMS.
    private string Fingerprint() =>
        string.Join(";", _kms.Credentials
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .Select(setting => $"{setting.Key}={(setting.Value is byte[] bytes ? Convert.ToBase64String(bytes) : setting.Value)}"));

    private sealed record ClientsKey(IMongoClient Source,
        CollectionNamespace KeyVault,
        string Kms,
        string Credentials,
        SslSettings? Tls);
}
