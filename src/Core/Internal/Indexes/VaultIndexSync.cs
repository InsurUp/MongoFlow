using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>
/// Syncs the indexes and search indexes of every collection the migrated vaults declare with what every registered vault
/// declares of it, for <see cref="MigrateOptions.SyncIndexes"/>, once the vaults' migrations have run. It plans every
/// collection before it changes any, then creates what's missing and changes in place what MongoDB can; makes indexes
/// unique, which fails while documents share their keys; recreates those that differ in what MongoDB can't change in
/// place; sets up search indexes; and drops what no vault declares last, so a failure before leaves it, and a query never
/// goes without the index a declared one replaces.
/// </summary>
internal sealed class VaultIndexSync(TimeProvider time)
{
    // The server has no search process, mongot.
    private const int SearchNotEnabled = 31082;

    // The server doesn't know an aggregation stage, as before 8.0.10 $listClusterCatalog.
    private const int UnrecognizedStage = 40324;

    // What can't fail on what's stored first; then making indexes unique, which can; then recreating them, which leaves
    // queries without one a moment.
    private static readonly SetupAction[][] Steps =
    [
        [SetupAction.Unchanged, SetupAction.Create, SetupAction.Update],
        [SetupAction.MakeUnique],
        [SetupAction.Recreate]
    ];

    private readonly SearchIndexSetup _search = new(time);

    public async Task RunAsync(IReadOnlyList<VaultModel> migrated,
        IReadOnlyList<VaultModel> registered,
        MigrateOptions options,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var synced = new List<Synced>();
        foreach (var declared in Collect(migrated, registered, errors))
        {
            if (await PlanAsync(declared, errors, cancellationToken) is { } plan)
            {
                synced.Add(plan);
            }
        }

        await CheckShardKeysAsync(synced, errors, cancellationToken);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Syncing indexes changed nothing: {string.Join(" ", errors)}");
        }

        // As the server doesn't create a collection for a search index, and would create one for an index without its
        // declared settings.
        foreach (var collection in synced.Where(collection =>
                     !collection.Exists && (collection.Indexes.Count > 0 || collection.Declared.SearchIndexes.Count > 0)))
        {
            await CollectionSettingsSync.EnsureCreatedAsync(collection.Declared.Model, collection.Declared.Collection, cancellationToken);
        }

        foreach (var actions in Steps)
        {
            foreach (var collection in synced)
            {
                foreach (var plan in collection.Indexes.Where(plan => actions.Contains(plan.Action)))
                {
                    await IndexSetup.ApplyAsync(collection.Declared.Model, collection.Declared.Collection, plan, cancellationToken);
                }
            }
        }

        await Task.WhenAll(synced.SelectMany(collection => collection.Declared.SearchIndexes.Select(index =>
            _search.SetUpAsync(collection.Declared.Model,
                collection.Name,
                collection.SearchIndexManager,
                index,
                collection.Search.GetValueOrDefault(index.Name),
                options.WaitForSearchIndexes,
                recreateOtherType: true,
                cancellationToken))));

        foreach (var collection in synced)
        {
            foreach (var index in collection.Dropping)
            {
                await IndexSetup.DropAsync(collection.Declared.Model, collection.Declared.Collection, index, cancellationToken);
            }

            foreach (var index in collection.SearchDropping)
            {
                await _search.DropAsync(collection.Declared.Model, collection.Name, collection.SearchIndexManager, index, cancellationToken);
            }
        }
    }

    // Every collection the migrated vaults declare, once whichever vaults map it, with what every registered vault declares
    // of it; an index of one name two declare otherwise is an error. It's set up through one that encrypts it, or else
    // declares its settings, or else was migrated.
    private static List<Declared> Collect(IReadOnlyList<VaultModel> migrated,
        IReadOnlyList<VaultModel> registered,
        List<string> errors)
    {
        var declaring = registered
            .SelectMany(model => model.Collections.Select(collection => (Model: model, Collection: collection)))
            .ToLookup(entry => Key(entry.Model, entry.Collection));

        return
        [
            .. migrated.SelectMany(model => model.Collections.Select(collection => Key(model, collection)))
                .Distinct()
                .Select(key =>
                {
                    var vaults = declaring[key].ToList();
                    var (model, collection) = vaults
                        .OrderByDescending(entry => entry.Collection.Encryption is not null)
                        .ThenByDescending(entry => entry.Collection.Settings is not null)
                        .ThenByDescending(entry => migrated.Contains(entry.Model))
                        .First();

                    return new Declared(model,
                        collection,
                        Union(vaults, entry => entry.Collection.Indexes, index => index.Name, (a, b) => a.Listed.Equals(b.Listed),
                            index => index.Definition, "index", errors),
                        Union(vaults, entry => entry.Collection.SearchIndexes, index => index.Name,
                            (a, b) => a.Type == b.Type && a.Definition.Equals(b.Definition),
                            index => $"{index.TypeName} {index.Definition.ToJson()}", "search index", errors));
                })
        ];
    }

    // Each declaration of a name once; two of one name that differ are an error, naming the vaults and how each declares it.
    private static List<T> Union<T>(List<(VaultModel Model, ICollectionModel Collection)> vaults,
        Func<(VaultModel Model, ICollectionModel Collection), IEnumerable<T>> declarations,
        Func<T, string> name,
        Func<T, T, bool> same,
        Func<T, string> describe,
        string kind,
        List<string> errors)
    {
        var union = new List<T>();
        var named = new Dictionary<string, (T Declaration, VaultModel Model)>(StringComparer.Ordinal);
        foreach (var entry in vaults)
        {
            foreach (var declaration in declarations(entry))
            {
                if (!named.TryGetValue(name(declaration), out var first))
                {
                    named.Add(name(declaration), (declaration, entry.Model));
                    union.Add(declaration);
                }
                else if (!same(first.Declaration, declaration))
                {
                    errors.Add($"{first.Model.VaultType.Name} declares the {kind} {name(declaration)} of {entry.Collection.Namespace.FullName} " +
                               $"as {describe(first.Declaration)}, and {entry.Model.VaultType.Name} as {describe(declaration)}.");
                }
            }
        }

        return union;
    }

    // What syncing a collection does, or nothing for a view, which has no indexes of its own.
    private static async Task<Synced?> PlanAsync(Declared declared,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var database = declared.Model.SourceDatabase;
        var name = declared.Collection.Namespace.CollectionName;
        if (await CollectionSettingsSync.ListAsync(database, name, cancellationToken) is { } listed && listed.GetValue("type", "collection") == "view")
        {
            return null;
        }

        var (exists, indexes, collation) = await IndexSync.ListAsync(database, name, cancellationToken);
        var names = declared.Indexes.Select(index => index.Name).ToHashSet(StringComparer.Ordinal);
        var plans = declared.Indexes.Select(index => IndexSpecs.PlanSync(index, indexes, collation, names)).ToList();
        errors.AddRange(plans.Where(plan => plan.Action == SetupAction.Conflict).Select(plan =>
            $"the index {plan.Declaration.Name} of {declared.Collection.Namespace.FullName}: {string.Join("; ", plan.Conflicts!)}."));

        var search = exists ? await ListSearchAsync(database, name, declared.SearchIndexes.Count > 0, cancellationToken) : [];
        var searchNames = declared.SearchIndexes.Select(index => index.Name).ToHashSet(StringComparer.Ordinal);

        return new Synced(declared,
            exists,
            plans,
            IndexSpecs.Undeclared(indexes, plans),
            search.ToDictionary(index => index["name"].AsString, StringComparer.Ordinal),
            [.. search.Where(index => !searchNames.Contains(index["name"].AsString))]);
    }

    /// <summary>
    /// The search indexes the server lists of <paramref name="collection"/>: none on a server without search, unless the
    /// collection <paramref name="declares"/> some, which then fails as setting them up would.
    /// </summary>
    public static async Task<List<BsonDocument>> ListSearchAsync(IMongoDatabase database,
        string collection,
        bool declares,
        CancellationToken cancellationToken)
    {
        try
        {
            return await (await database.GetCollection<BsonDocument>(collection).SearchIndexes.ListAsync(cancellationToken: cancellationToken))
                .ToListAsync(cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code == SearchNotEnabled && !declares)
        {
            return [];
        }
    }

    // An error for each index syncing would drop that is the one on a sharded collection's shard key, which no declared
    // index has the keys of: the server drops one on a hashed shard key, though the collection's chunks are split by it.
    private static async Task CheckShardKeysAsync(List<Synced> synced,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (var database in synced.Where(collection => collection.Dropping.Count > 0)
                     .GroupBy(collection => Key(collection.Declared.Model, collection.Declared.Collection) with { Collection = "" }))
        {
            var shardKeys = await ShardKeysAsync(database.First().Declared.Model.SourceDatabase,
                database.Select(collection => collection.Declared.Collection.Namespace.FullName),
                cancellationToken);

            foreach (var collection in database)
            {
                var name = collection.Declared.Collection.Namespace.FullName;
                if (!shardKeys.TryGetValue(name, out var shardKey) ||
                    collection.Declared.Indexes.Any(index => IndexSpecs.Backs(index.Listed["key"].AsBsonDocument, shardKey)))
                {
                    continue;
                }

                errors.AddRange(collection.Dropping
                    .Where(index => IndexSpecs.Backs(index["key"].AsBsonDocument, shardKey))
                    .Select(index => $"the index {IndexSpecs.NameOf(index)} of {name} is on its shard key, " +
                                     $"{shardKey.ToJson()}, and no vault declares one that is: declare it."));
            }
        }
    }

    /// <summary>
    /// The shard keys of the sharded collections of <paramref name="namespaces"/>, by namespace, as
    /// <c>$listClusterCatalog</c> lists them, which needs no more than reading the database: none on a replica set.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server has no <c>$listClusterCatalog</c>, as before MongoDB 8.0.10.</exception>
    public static async Task<Dictionary<string, BsonDocument>> ShardKeysAsync(IMongoDatabase database,
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken)
    {
        var pipeline = PipelineDefinition<NoPipelineInput, BsonDocument>.Create(
            new BsonDocument("$listClusterCatalog", new BsonDocument()),
            new BsonDocument("$match", new BsonDocument { { "ns", new BsonDocument("$in", new BsonArray(namespaces)) }, { "sharded", true } }),
            new BsonDocument("$project", new BsonDocument { { "ns", 1 }, { "shardKey", 1 } }));

        try
        {
            var sharded = await (await database.AggregateAsync(pipeline, cancellationToken: cancellationToken)).ToListAsync(cancellationToken);
            return sharded.ToDictionary(collection => collection["ns"].AsString, collection => collection["shardKey"].AsBsonDocument,
                StringComparer.Ordinal);
        }
        catch (MongoCommandException exception) when (exception.Code == UnrecognizedStage)
        {
            throw new InvalidOperationException(
                "Syncing indexes asks the server which collections are sharded, to keep the indexes their shard keys are on, " +
                "with $listClusterCatalog, which MongoDB has from 8.0.10. Upgrade it, or migrate without MigrateOptions.SyncIndexes.",
                exception);
        }
    }

    // A collection, whichever vault and client reach it: the servers it's on, its database and its name.
    private static (string Servers, string Database, string Collection) Key(VaultModel model,
        ICollectionModel collection) =>
        (string.Join(",", model.SourceDatabase.Client.Settings.Servers),
            collection.Namespace.DatabaseNamespace.DatabaseName,
            collection.Namespace.CollectionName);

    // A collection the migrated vaults declare, with every declaration of it, and the vault and collection that set it up.
    private sealed record Declared(VaultModel Model,
        ICollectionModel Collection,
        IReadOnlyList<IndexDeclaration> Indexes,
        IReadOnlyList<SearchIndexDeclaration> SearchIndexes);

    // What syncing a collection does: each declared index's plan, the indexes it drops, and the search indexes listed and
    // dropped.
    private sealed record Synced(Declared Declared,
        bool Exists,
        IReadOnlyList<IndexPlan> Indexes,
        IReadOnlyList<BsonDocument> Dropping,
        IReadOnlyDictionary<string, BsonDocument> Search,
        IReadOnlyList<BsonDocument> SearchDropping)
    {
        public string Name => Declared.Collection.Namespace.CollectionName;

        public IMongoSearchIndexManager SearchIndexManager =>
            Declared.Model.SourceDatabase.GetCollection<BsonDocument>(Name).SearchIndexes;
    }
}
