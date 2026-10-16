using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>Creates a search index, or updates it when it's defined otherwise, and waits for one to be queryable.</summary>
internal static class SearchIndexSync
{
    private const int IndexAlreadyExists = 68;

    /// <summary>
    /// Creates the index named <paramref name="name"/>, or updates it when <paramref name="listed"/>, as
    /// <c>$listSearchIndexes</c> shows it, is defined otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">The index is of the other type, which can't be changed.</exception>
    public static async Task<SearchIndexChange> CreateOrUpdateAsync(IMongoSearchIndexManager indexes,
        string name,
        BsonDocument definition,
        SearchIndexType type,
        BsonDocument? listed,
        CancellationToken cancellationToken)
    {
        if (listed is null)
        {
            try
            {
                await indexes.CreateOneAsync(new CreateSearchIndexModel(name, type, definition), cancellationToken);
                return SearchIndexChange.Created;
            }
            catch (MongoCommandException exception) when (exception.Code == IndexAlreadyExists)
            {
                // Another instance created it meanwhile; it's compared with what this one declares, as if it were listed.
                listed = await ListAsync(indexes, name, cancellationToken);
                if (listed is null)
                {
                    throw;
                }
            }
        }

        var listedType = listed.GetValue("type", "search").ToString();
        var typeName = TypeName(type);
        if (listedType != typeName)
        {
            throw new InvalidOperationException(
                $"The search index {name} is a {listedType} index, but is declared a {typeName} index, and an index's type " +
                "can't be changed. Drop it, or name the declared one otherwise.");
        }

        if (SearchIndexDefinitions.Matches(definition, listed.GetValue("latestDefinition", BsonNull.Value) as BsonDocument ?? [], type))
        {
            return SearchIndexChange.Unchanged;
        }

        await indexes.UpdateAsync(name, definition, cancellationToken);
        return SearchIndexChange.Updated;
    }

    /// <summary>Whether <paramref name="listed"/>, as <c>$listSearchIndexes</c> shows an index, is built and queryable.</summary>
    public static bool IsQueryable(BsonDocument listed) =>
        listed.GetValue("status", BsonNull.Value) == "READY" && listed.GetValue("queryable", false).ToBoolean();

    /// <summary>
    /// Waits until the index named <paramref name="name"/> is built and queryable, its latest definition included, checking
    /// every second at first, then less often, up to every five.
    /// </summary>
    /// <param name="indexes">The collection's search indexes.</param>
    /// <param name="name">The index's name.</param>
    /// <param name="time">What the checks wait with.</param>
    /// <param name="building">Called once, with the index's status, if it isn't queryable at first.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <exception cref="InvalidOperationException">The index failed to build, or was dropped.</exception>
    public static async Task WaitAsync(IMongoSearchIndexManager indexes,
        string name,
        TimeProvider time,
        Action<string>? building,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var first = true;

        while (true)
        {
            var listed = await ListAsync(indexes, name, cancellationToken) ??
                         throw new InvalidOperationException(
                             $"There's no search index named {name}: it wasn't created, or was dropped while waiting for it.");

            if (IsQueryable(listed))
            {
                return;
            }

            var status = listed.GetValue("status", "PENDING").ToString()!;
            if (status == "FAILED")
            {
                throw new InvalidOperationException($"The search index {name} failed to build: {FailureOf(listed)}");
            }

            if (first)
            {
                building?.Invoke(status);
                first = false;
            }

            await Task.Delay(delay, time, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(5).Ticks));
        }
    }

    /// <summary>The index named <paramref name="name"/> as <c>$listSearchIndexes</c> shows it, or <see langword="null"/>.</summary>
    public static async Task<BsonDocument?> ListAsync(IMongoSearchIndexManager indexes,
        string name,
        CancellationToken cancellationToken) =>
        await (await indexes.ListAsync(name, cancellationToken: cancellationToken)).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The type as <c>$listSearchIndexes</c> names it.</summary>
    public static string TypeName(SearchIndexType type) => type == SearchIndexType.VectorSearch ? "vectorSearch" : "search";

    // Why the server says an index failed: its message, or its details per host.
    private static string FailureOf(BsonDocument listed) =>
        listed.GetValue("message", BsonNull.Value) is BsonString message
            ? message.Value
            : listed.GetValue("statusDetail", BsonNull.Value) is BsonArray { Count: > 0 } details
                ? details.ToJson()
                : "the server gives no reason.";
}
