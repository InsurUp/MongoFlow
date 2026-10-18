using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Lists, creates, changes and recreates a collection's indexes as declared, on any database.</summary>
internal static class IndexSync
{
    /// <summary>
    /// Whether the collection exists, the indexes the server lists of it, and the collation they compare strings with
    /// unless they say otherwise.
    /// </summary>
    public static async Task<(bool Exists, IReadOnlyList<BsonDocument> Indexes, BsonDocument? Collation)> ListAsync(
        IMongoDatabase database,
        string collection,
        CancellationToken cancellationToken)
    {
        if (await CollectionSettingsSync.ListAsync(database, collection, cancellationToken) is not { } listed)
        {
            return (false, [], null);
        }

        var indexes = await (await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync(cancellationToken))
            .ToListAsync(cancellationToken);
        var options = listed.GetValue("options", new BsonDocument()).AsBsonDocument;

        return (true, indexes, options.GetValue("collation", BsonNull.Value) as BsonDocument);
    }

    /// <summary>Creates the index <paramref name="plan"/> creates, changes it in place, or makes it unique.</summary>
    public static async Task ApplyAsync(IMongoDatabase database,
        string collection,
        IndexPlan plan,
        CancellationToken cancellationToken)
    {
        var indexes = database.GetCollection<BsonDocument>(collection).Indexes;
        switch (plan.Action)
        {
            case SetupAction.Create:
                await indexes.CreateOneAsync(plan.Declaration.Model(), cancellationToken: cancellationToken);
                break;

            case SetupAction.Update:
                await ModifyAsync(database, collection, plan.Changes!, cancellationToken);
                break;

            case SetupAction.MakeUnique:
                await MakeUniqueAsync(database, collection, plan, cancellationToken);
                break;
        }
    }

    /// <summary>Drops the index named <paramref name="name"/>.</summary>
    public static Task DropAsync(IMongoDatabase database,
        string collection,
        string name,
        CancellationToken cancellationToken) =>
        database.GetCollection<BsonDocument>(collection).Indexes.DropOneAsync(name, cancellationToken);

    /// <summary>
    /// Drops the index named <paramref name="name"/>, and any the server would refuse <paramref name="declared"/> beside,
    /// then creates <paramref name="declared"/>, if given.
    /// </summary>
    /// <returns>The names of the indexes dropped.</returns>
    public static async Task<IReadOnlyList<string>> RecreateAsync(IMongoDatabase database,
        string collection,
        string name,
        IndexDeclaration? declared,
        CancellationToken cancellationToken)
    {
        var (_, indexes, collation) = await ListAsync(database, collection, cancellationToken);
        var dropping = indexes
            .Select(index => (Name: IndexSpecs.NameOf(index), Index: index))
            .Where(index => index.Name is { } listed and not "_id_" &&
                            (listed == name || (declared is not null && IndexSpecs.Equivalent(declared, index.Index, collation))))
            .Select(index => index.Name!)
            .ToList();

        var manager = database.GetCollection<BsonDocument>(collection).Indexes;
        foreach (var dropped in dropping)
        {
            await manager.DropOneAsync(dropped, cancellationToken);
        }

        if (declared is not null)
        {
            await manager.CreateOneAsync(declared.Model(), cancellationToken: cancellationToken);
        }

        return dropping;
    }

    // Unique in two steps, as collMod takes it: the index refuses new duplicates first, then becomes unique, which fails
    // while documents share its keys, and the index is put back as it was.
    private static async Task MakeUniqueAsync(IMongoDatabase database,
        string collection,
        IndexPlan plan,
        CancellationToken cancellationToken)
    {
        var name = plan.Declaration.Name;
        if (plan.Changes is { } changes)
        {
            await ModifyAsync(database, collection, changes, cancellationToken);
        }

        await ModifyAsync(database, collection, new BsonDocument { { "name", name }, { "prepareUnique", true } }, cancellationToken);
        try
        {
            await ModifyAsync(database, collection, new BsonDocument { { "name", name }, { "unique", true } }, cancellationToken);
        }
        catch (MongoCommandException)
        {
            await ModifyAsync(database, collection, new BsonDocument { { "name", name }, { "prepareUnique", false } }, CancellationToken.None);
            throw;
        }
    }

    private static Task ModifyAsync(IMongoDatabase database,
        string collection,
        BsonDocument index,
        CancellationToken cancellationToken) =>
        database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", collection }, { "index", index } },
            cancellationToken: cancellationToken);
}
