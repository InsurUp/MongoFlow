using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

internal static class BulkWriteSupport
{
    // 25 is MongoDB 8.0, the first server with client bulk writes.
    private const int ClientBulkWriteWireVersion = 25;

    private static readonly ConditionalWeakTable<IMongoClient, object> Supported = new();

    public static async Task EnsureAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        if (Supported.TryGetValue(database.Client, out _))
        {
            return;
        }

        var hello = await database.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: cancellationToken);
        var wireVersion = hello.GetValue("maxWireVersion", 0).ToInt32();

        if (wireVersion < ClientBulkWriteWireVersion)
        {
            throw new NotSupportedException(
                $"MongoFlow saves with client bulk writes, which need MongoDB 8.0 or later; the server reports wire version {wireVersion}.");
        }

        Supported.TryAdd(database.Client, true);
    }
}
