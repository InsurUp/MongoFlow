using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Warns, once per model, about collections missing the indexes their key and features rely on: a key other than
/// <c>_id</c> needs a unique index covering it, and a field a feature filters every read on needs to be in an index.
/// MongoFlow doesn't create indexes; it only says which are missing.
/// </summary>
internal static class IndexCheck
{
    /// <summary>Starts the check in the background, when the warnings would be logged.</summary>
    public static void Start(VaultModel model)
    {
        if (model.Logs.Indexes.IsEnabled(LogLevel.Warning))
        {
            _ = Task.Run(() => RunAsync(model));
        }
    }

    private static async Task RunAsync(VaultModel model)
    {
        var log = model.Logs.Indexes;

        foreach (var collection in model.Collections)
        {
            var name = collection.Namespace.FullName;
            try
            {
                var key = collection.KeyFields is ["_id"] ? null : collection.KeyFields;
                var fields = collection.RenderIndexedFields().ToList();
                if (key is null && fields.Count == 0)
                {
                    continue;
                }

                var cursor = await model.Database.GetCollection<BsonDocument>(collection.Namespace.CollectionName).Indexes.ListAsync();
                var indexes = await cursor.ToListAsync();

                if (indexes.Count == 0)
                {
                    log.IndexCheckSkipped(name);
                    continue;
                }

                if (key is not null)
                {
                    CheckKey(log, name, key, indexes);
                }

                foreach (var field in fields.Where(field => !indexes.Any(index => Includes(index, field.Field))))
                {
                    log.FieldIndexMissing(name, field.Field, field.Feature);
                }
            }
            catch (Exception exception)
            {
                log.IndexCheckFailed(name, exception);
            }
        }

        log.IndexCheckCompleted(model.VaultType.Name);
    }

    // An equality on every key field uses an index that starts with them, in any order; it's unique on the key only if it
    // has no other fields.
    private static void CheckKey(ILogger log,
        string collection,
        IReadOnlyList<string> key,
        List<BsonDocument> indexes)
    {
        var covering = indexes.Where(index => Fields(index).Take(key.Count).Order().SequenceEqual(key.Order())).ToList();

        if (covering.Count == 0)
        {
            log.KeyIndexMissing(collection, key);
        }
        else if (!covering.Any(index => index.GetValue("unique", false).ToBoolean() && Fields(index).Count() == key.Count))
        {
            log.KeyIndexNotUnique(collection, key, covering[0].GetValue("name", "").ToString()!);
        }
    }

    // A wildcard index covers every field.
    private static bool Includes(BsonDocument index,
        string field) =>
        Fields(index).Any(indexed => indexed == field || indexed.EndsWith("$**", StringComparison.Ordinal));

    private static IEnumerable<string> Fields(BsonDocument index) => index["key"].AsBsonDocument.Names;
}
