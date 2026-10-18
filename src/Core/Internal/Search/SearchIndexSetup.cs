using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>
/// Sets up the search indexes a vault's collections declare, after its migrations: creates those missing, updates those
/// defined otherwise, and unless <see cref="MigrateOptions.WaitForSearchIndexes"/> says not to, waits until every one is
/// queryable, so searches find what's there once the migrator returns.
/// </summary>
internal sealed class SearchIndexSetup(TimeProvider time) : IVaultSetup
{
    public bool BeforeMigrations => false;

    public async Task RunAsync(VaultModel model,
        MigrateOptions options,
        CancellationToken cancellationToken)
    {
        // Synced instead, with every vault's declarations, once every vault's migrations have run.
        if (options.SyncIndexes)
        {
            return;
        }

        var settingUp = new List<Task>();

        foreach (var collection in model.Collections.Where(collection => collection.SearchIndexes.Count > 0))
        {
            var name = collection.Namespace.CollectionName;
            var indexes = model.SourceDatabase.GetCollection<BsonDocument>(name).SearchIndexes;

            // The server doesn't create a collection for a search index, as it does for an index.
            await CollectionSettingsSync.EnsureCreatedAsync(model, collection, cancellationToken);

            var listed = await (await indexes.ListAsync(cancellationToken: cancellationToken)).ToListAsync(cancellationToken);
            var byName = listed.Where(index => index.GetValue("name", BsonNull.Value).IsString)
                .ToDictionary(index => index["name"].AsString, StringComparer.Ordinal);

            settingUp.AddRange(collection.SearchIndexes.Select(index =>
                SetUpAsync(model, name, indexes, index, byName.GetValueOrDefault(index.Name), options.WaitForSearchIndexes,
                    recreateOtherType: false, cancellationToken)));
        }

        await Task.WhenAll(settingUp);
    }

    /// <summary>
    /// Creates <paramref name="index"/>, or updates it when <paramref name="listed"/> is defined otherwise, in a span of its
    /// own, and waits until it's queryable if <paramref name="wait"/> says to. One listed of the other type fails, or, with
    /// <paramref name="recreateOtherType"/>, as syncing indexes does, is dropped and created anew.
    /// </summary>
    public async Task SetUpAsync(VaultModel model,
        string collection,
        IMongoSearchIndexManager indexes,
        SearchIndexDeclaration index,
        BsonDocument? listed,
        bool wait,
        bool recreateOtherType,
        CancellationToken cancellationToken)
    {
        var log = model.Logs.Search;
        using var activity = VaultActivities.StartSearchIndex(model.VaultType.Name, collection, index);
        try
        {
            var recreated = false;
            if (recreateOtherType && listed is not null && SearchIndexSync.TypeOf(listed) is var type && type != index.Type)
            {
                await SearchIndexSync.DropAsync(indexes, index.Name, time, cancellationToken);
                log.IndexDropped(SearchIndexSync.TypeName(type), index.Name, collection,
                    $"to create it anew as a {index.TypeName} index, as an index's type can't be changed", DefinitionOf(listed));
                (listed, recreated) = (null, true);
            }

            var change = await SearchIndexSync.CreateOrUpdateAsync(indexes, index.Name, index.Definition, index.Type, listed,
                cancellationToken);
            if (recreated)
            {
                activity.Changed(SetupAction.Recreate);
            }
            else
            {
                activity.Changed(change);
            }

            switch (change)
            {
                case IndexChange.Created:
                    log.IndexCreated(index.TypeName, index.Name, collection);
                    break;

                case IndexChange.Updated:
                    log.IndexUpdated(index.TypeName, index.Name, collection);
                    break;

                default:
                    log.IndexUpToDate(index.TypeName, index.Name, collection);
                    break;
            }

            // One unchanged may still be building, created by another instance or before a restart.
            if (wait && (change != IndexChange.Unchanged || listed is null || !SearchIndexSync.IsQueryable(listed)))
            {
                await SearchIndexSync.WaitAsync(indexes,
                    index.Name,
                    time,
                    status => log.IndexBuilding(index.TypeName, index.Name, collection, status),
                    cancellationToken);

                log.IndexQueryable(index.TypeName, index.Name, collection);
            }
        }
        catch (Exception exception)
        {
            if (exception is InvalidOperationException && !cancellationToken.IsCancellationRequested)
            {
                log.IndexFailed(index.TypeName, index.Name, collection, exception.Message);
            }

            activity.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Drops <paramref name="listed"/>, a search index no vault declares, in a span of its own, logging its definition.
    /// </summary>
    public async Task DropAsync(VaultModel model,
        string collection,
        IMongoSearchIndexManager indexes,
        BsonDocument listed,
        CancellationToken cancellationToken)
    {
        var name = listed["name"].AsString;
        var type = SearchIndexSync.TypeName(SearchIndexSync.TypeOf(listed));

        using var activity = VaultActivities.StartSearchIndex(model.VaultType.Name, collection, name, type);
        try
        {
            await indexes.DropOneAsync(name, cancellationToken);
            activity.Changed(SetupAction.Drop);
            model.Logs.Search.IndexDropped(type, name, collection, "which no vault declares", DefinitionOf(listed));
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    // What the server lists the index as built to, which creates it again.
    private static string DefinitionOf(BsonDocument listed) => listed.GetValue("latestDefinition", BsonNull.Value).ToJson();
}
