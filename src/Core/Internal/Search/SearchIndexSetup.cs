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
                    cancellationToken)));
        }

        await Task.WhenAll(settingUp);
    }

    private async Task SetUpAsync(VaultModel model,
        string collection,
        IMongoSearchIndexManager indexes,
        SearchIndexDeclaration index,
        BsonDocument? listed,
        bool wait,
        CancellationToken cancellationToken)
    {
        var log = model.Logs.Search;
        using var activity = VaultActivities.StartSearchIndex(model.VaultType.Name, collection, index);
        try
        {
            var change = await SearchIndexSync.CreateOrUpdateAsync(indexes, index.Name, index.Definition, index.Type, listed,
                cancellationToken);
            activity.Changed(change);

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
}
