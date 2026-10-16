using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Encryption;

/// <summary>
/// Sets up one encrypted collection, for the migrator: creates it, with its data keys, if it's missing; checks it's
/// encrypted as declared if it is; and rebuilds it as an encrypted collection if it isn't, which MongoDB can't do in
/// place.
/// </summary>
/// <remarks>
/// A rebuild renames the collection to <c>{name}.encrypting</c>, creates the encrypted collection under the name, copies the
/// documents into it, encrypted, and the indexes that don't cover an encrypted field, then renames the copy to
/// <c>{name}.unencrypted</c>, which is how a later run knows it's done. One interrupted goes on from where it stopped. The
/// unencrypted copy is never dropped: it holds the data as it was.
/// </remarks>
internal sealed class CollectionSetup(CollectionEncryption collection,
    EncryptionClients clients,
    IMongoDatabase source,
    ILogger log,
    string kmsProvider,
    BsonDocument? masterKey)
{
    private const int BatchSize = 1000;
    private const int NamespaceExists = 48;

    private readonly string _name = collection.Namespace.CollectionName;

    private string Encrypting => $"{_name}.encrypting";

    private string Unencrypted => $"{_name}.unencrypted";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stored = await ListAsync(cancellationToken);
        stored.TryGetValue(_name, out var current);

        if (stored.ContainsKey(Encrypting))
        {
            await RebuildAsync(current, resumed: true, cancellationToken);
            return;
        }

        switch (current)
        {
            case null:
                await CreateAsync(cancellationToken);
                break;

            case { EncryptedFields: { } encryptedFields }:
                // Throws if it's encrypted otherwise than declared.
                collection.KeysOf(encryptedFields);
                log.CollectionUpToDate(collection.Namespace.FullName);
                break;

            default:
                await RebuildAsync(current, resumed: false, cancellationToken);
                return;
        }

        if (stored.ContainsKey(Unencrypted))
        {
            log.UnencryptedCopyRemains(Unencrypted, collection.Namespace.FullName);
        }
    }

    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        await clients.ClientEncryption.CreateEncryptedCollectionAsync(source,
            _name,
            new CreateCollectionOptions { EncryptedFields = collection.Schema },
            kmsProvider,
            masterKey!,
            cancellationToken);

        collection.ForgetKeys();
        log.CollectionCreated(collection.Namespace.FullName, string.Join(", ", collection.Fields));
    }

    private async Task RebuildAsync(StoredCollection? current,
        bool resumed,
        CancellationToken cancellationToken)
    {
        log.RebuildStarted(collection.Namespace.FullName, Encrypting, resumed);

        // Listed before the rename, which the search process learns of a moment after it.
        var searchIndexes = await SearchIndexesAsync(resumed ? Encrypting : _name, cancellationToken);

        if (!resumed)
        {
            await source.RenameCollectionAsync(_name, Encrypting, cancellationToken: cancellationToken);
            current = null;
        }

        switch (current)
        {
            case null:
                await CreateAsync(cancellationToken);
                break;

            case { EncryptedFields: { } encryptedFields }:
                collection.KeysOf(encryptedFields);
                break;

            default:
                throw new InvalidOperationException(
                    $"{_name} isn't encrypted, while {Encrypting} holds the documents a rebuild was " +
                    "copying into it: something wrote to it meanwhile, which a rebuild needs nothing to do. Move its " +
                    $"documents into {Encrypting}, drop it, and migrate again.");
        }

        var copied = await CopyAsync(resumed, cancellationToken);
        var indexes = await CopyIndexesAsync(cancellationToken);

        try
        {
            await source.RenameCollectionAsync(Encrypting, Unencrypted, cancellationToken: cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code == NamespaceExists)
        {
            throw new InvalidOperationException(
                $"{_name} is rebuilt as an encrypted collection, but its unencrypted copy can't be " +
                $"kept as {Unencrypted}, which exists already. Drop or rename {Unencrypted}, and migrate again: what's left is " +
                $"renaming {Encrypting}.",
                exception);
        }

        log.RebuildCompleted(collection.Namespace.FullName, copied, indexes);
        log.UnencryptedCopyRemains(Unencrypted, collection.Namespace.FullName);

        if (searchIndexes.Count > 0)
        {
            log.SearchIndexesFollowCopy(collection.Namespace.FullName, searchIndexes, Unencrypted);
        }
    }

    /// <summary>
    /// The names of <paramref name="name"/>'s MongoDB Search indexes, which follow it when it's renamed; none on a server
    /// without MongoDB Search, which refuses to list them.
    /// </summary>
    private async Task<IReadOnlyList<string>> SearchIndexesAsync(string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var listed = await (await source.GetCollection<BsonDocument>(name).SearchIndexes.ListAsync(cancellationToken: cancellationToken))
                .ToListAsync(cancellationToken);

            return [.. listed.Select(index => index.GetValue("name", "").ToString()!)];
        }
        catch (MongoCommandException)
        {
            return [];
        }
    }

    /// <summary>
    /// Copies the documents of the unencrypted copy into the encrypted collection, in batches, in <c>_id</c> order. One
    /// that goes on with an interrupted rebuild skips those copied already.
    /// </summary>
    private async Task<long> CopyAsync(bool resumed,
        CancellationToken cancellationToken)
    {
        var from = source.GetCollection<BsonDocument>(Encrypting);
        var into = clients.Encrypting.GetDatabase(source.DatabaseNamespace.DatabaseName).GetCollection<BsonDocument>(_name);
        var copiedBefore = source.GetCollection<BsonDocument>(_name);

        long copied = 0;
        var batch = new List<BsonDocument>(BatchSize);
        using var cursor = await from.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("_id", 1))
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            batch.AddRange(cursor.Current);

            while (batch.Count >= BatchSize)
            {
                copied += await InsertAsync(batch.GetRange(0, BatchSize));
                batch.RemoveRange(0, BatchSize);
            }
        }

        if (batch.Count > 0)
        {
            copied += await InsertAsync(batch);
        }

        return copied;

        async Task<long> InsertAsync(List<BsonDocument> documents)
        {
            if (resumed)
            {
                var ids = new BsonArray(documents.Select(document => document["_id"]));
                var there = await copiedBefore.Find(new BsonDocument("_id", new BsonDocument("$in", ids)))
                    .Project(new BsonDocument("_id", 1))
                    .ToListAsync(cancellationToken);
                var skipped = there.Select(document => document["_id"]).ToHashSet();

                documents = documents.Where(document => !skipped.Contains(document["_id"])).ToList();
            }

            if (documents.Count == 0)
            {
                return 0;
            }

            foreach (var document in documents)
            {
                await collection.EncryptDocumentAsync(document, cancellationToken);
            }

            await into.InsertManyAsync(documents, cancellationToken: cancellationToken);
            log.RebuildProgress(collection.Namespace.FullName, copied + documents.Count);

            return documents.Count;
        }
    }

    /// <summary>
    /// Creates the unencrypted copy's indexes on the encrypted collection, apart from those covering an encrypted field;
    /// returns how many it has.
    /// </summary>
    private async Task<int> CopyIndexesAsync(CancellationToken cancellationToken)
    {
        var indexes = await (await source.GetCollection<BsonDocument>(Encrypting).Indexes.ListAsync(cancellationToken))
            .ToListAsync(cancellationToken);
        var copying = new BsonArray();

        foreach (var index in indexes)
        {
            var name = index.GetValue("name", "").AsString;
            if (name == "_id_")
            {
                continue;
            }

            var covered = index["key"].AsBsonDocument.Names
                .Concat(index.TryGetValue("partialFilterExpression", out var partial) ? Paths(partial) : [])
                .Select(path => collection.Paths.FirstOrDefault(encrypted => path == "$**" || ElementPaths.Reaches(path, encrypted)))
                .FirstOrDefault(encrypted => encrypted is not null);

            if (covered is not null)
            {
                log.IndexSkipped(name, collection.Namespace.FullName, covered);
                continue;
            }

            var spec = (BsonDocument)index.DeepClone();
            spec.Remove("v");
            spec.Remove("ns");
            copying.Add(spec);
        }

        if (copying.Count > 0)
        {
            await source.RunCommandAsync<BsonDocument>(new BsonDocument { { "createIndexes", _name }, { "indexes", copying } },
                cancellationToken: cancellationToken);
        }

        return copying.Count;
    }

    // The fields a filter compares, at any depth.
    private static IEnumerable<string> Paths(BsonValue filter) =>
        filter is BsonDocument document
            ? document.Elements.SelectMany(element => element.Name.StartsWith('$')
                ? element.Value is BsonArray clauses ? clauses.SelectMany(Paths) : []
                : [element.Name])
            : [];

    private async Task<Dictionary<string, StoredCollection>> ListAsync(CancellationToken cancellationToken)
    {
        var options = new ListCollectionsOptions
        {
            Filter = new BsonDocument("name", new BsonDocument("$in", new BsonArray { _name, Encrypting, Unencrypted }))
        };
        var listed = await (await source.ListCollectionsAsync(options, cancellationToken)).ToListAsync(cancellationToken);

        return listed.ToDictionary(stored => stored["name"].AsString,
            stored => new StoredCollection(stored.GetValue("options", new BsonDocument()).AsBsonDocument
                .GetValue("encryptedFields", null) as BsonDocument));
    }

    private sealed record StoredCollection(BsonDocument? EncryptedFields);
}
