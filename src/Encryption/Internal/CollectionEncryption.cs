using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// How a collection's encrypted members are written and queried. The fields' data keys are read, once, from the schema
/// the server holds for the collection, which has to be the one the vault declares: the migrator creates it.
/// </summary>
internal sealed class CollectionEncryption : ICollectionEncryption
{
    private readonly IMongoDatabase _source;
    private readonly EncryptionClients _clients;
    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile IReadOnlyDictionary<string, Guid>? _keys;

    public CollectionEncryption(CollectionNamespace @namespace,
        IReadOnlyList<FieldSchema> fields,
        EncryptionClients clients)
    {
        Namespace = @namespace;
        Fields = fields;
        Paths = [.. fields.Select(field => field.Path)];
        _clients = clients;
        _source = clients.Source.GetDatabase(@namespace.DatabaseNamespace.DatabaseName);
    }

    public CollectionNamespace Namespace { get; }

    public IReadOnlyList<FieldSchema> Fields { get; }

    public IReadOnlyList<string> Paths { get; }

    // The server changes encrypted documents only one at a time.
    public bool SingleDocumentWrites => true;

    /// <summary>The schema the server holds the collection with, as the vault declares it, with no data keys yet.</summary>
    public BsonDocument Schema => new("fields", new BsonArray(Fields.Select(each => each.ToDocument(BsonNull.Value))));

    public async ValueTask EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_keys is not null)
        {
            return;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            _keys ??= await LoadKeysAsync(cancellationToken);
        }
        finally
        {
            _loading.Release();
        }
    }

    public async ValueTask<BsonDocument> EncryptDocumentAsync(BsonDocument document,
        CancellationToken cancellationToken) =>
        await (await EncryptionAsync(cancellationToken)).DocumentAsync(document, cancellationToken);

    public async ValueTask<BsonDocument> EncryptFilterAsync(BsonDocument filter,
        CancellationToken cancellationToken) =>
        await (await EncryptionAsync(cancellationToken)).FilterAsync(filter, cancellationToken);

    public BsonDocument EncryptFilter(BsonDocument filter)
    {
        var keys = _keys ?? throw new InvalidOperationException(
            $"The data keys of {Namespace.CollectionName} aren't loaded: EnsureReadyAsync runs before a query that encrypts.");

        // Its encryptor completes before returning.
        return new FieldEncryption(Namespace.CollectionName, Fields, keys, new ClientEncryptor(_clients.ClientEncryption, synchronous: true))
            .FilterAsync(filter, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask<BsonValue> EncryptUpdateAsync(BsonValue update,
        CancellationToken cancellationToken) =>
        await (await EncryptionAsync(cancellationToken)).UpdateAsync(update, cancellationToken);

    /// <summary>Forgets the data keys, for the migrator, which has just created the collection with new ones.</summary>
    public void ForgetKeys() => _keys = null;

    private async ValueTask<FieldEncryption> EncryptionAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);

        return new FieldEncryption(Namespace.CollectionName, Fields, _keys!, new ClientEncryptor(_clients.ClientEncryption, synchronous: false));
    }

    private async Task<IReadOnlyDictionary<string, Guid>> LoadKeysAsync(CancellationToken cancellationToken)
    {
        var name = Namespace.CollectionName;
        var options = new ListCollectionsOptions { Filter = new BsonDocument("name", name) };
        var stored = await (await _source.ListCollectionsAsync(options, cancellationToken)).FirstOrDefaultAsync(cancellationToken);

        if (stored?.GetValue("options", null)?.AsBsonDocument.GetValue("encryptedFields", null) is not BsonDocument encryptedFields)
        {
            throw new InvalidOperationException(stored is null
                ? $"{Namespace.CollectionName} doesn't exist, so it isn't encrypted yet. Run IVaultMigrator.MigrateAllAsync before " +
                  "using the vault: it creates the encrypted collections."
                : $"{Namespace.CollectionName} isn't an encrypted collection. Run IVaultMigrator.MigrateAllAsync before using the " +
                  "vault: it rebuilds a collection as an encrypted one.");
        }

        return KeysOf(encryptedFields);
    }

    /// <summary>The data key of each field in <paramref name="encryptedFields"/>, the server's schema, once it's checked.</summary>
    /// <exception cref="InvalidOperationException">It isn't the schema the vault declares.</exception>
    public IReadOnlyDictionary<string, Guid> KeysOf(BsonDocument encryptedFields)
    {
        var stored = encryptedFields.GetValue("fields", new BsonArray()).AsBsonArray
            .OfType<BsonDocument>()
            .ToDictionary(field => field.GetValue("path", "").AsString);

        var differences = new List<string>();
        foreach (var field in Fields)
        {
            if (!stored.TryGetValue(field.Path, out var held))
            {
                differences.Add($"{field.Path} isn't encrypted");
            }
            else if (!field.Matches(held))
            {
                differences.Add($"{field.Path} is encrypted as {FieldSchema.Describe(held)}, not as {field.Describe()}");
            }
        }

        differences.AddRange(stored.Keys.Where(path => Fields.All(field => field.Path != path))
            .Select(path => $"{path} is encrypted, though the vault doesn't declare it"));

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"{Namespace.CollectionName} is encrypted differently from how the vault declares it: {string.Join("; ", differences)}. " +
                "MongoDB can't change an encrypted collection's fields: copy its documents into a new collection, under " +
                "another name, encrypted as declared.");
        }

        return Fields.ToDictionary(field => field.Path, field => stored[field.Path]["keyId"].AsGuid);
    }
}
