using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// A client of a server that isn't there. MongoFlow only talks to the server to save or run a query, so it's enough to
/// configure vaults, queue writes and build queries; anything that does reach for the server fails within a second.
/// </summary>
public static class Offline
{
    public const string DatabaseName = "unit";

    public static IMongoClient Client { get; } =
        new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500&connectTimeoutMS=500");

    public static IMongoDatabase Database { get; } = Client.GetDatabase(DatabaseName);
}
