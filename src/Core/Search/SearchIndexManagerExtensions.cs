using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>
/// Search indexes managed by hand, in a migration for instance, through a collection's <c>SearchIndexes</c>. Indexes a
/// collection declares with <c>SearchIndex</c> and <c>VectorSearchIndex</c> need neither: the migrator does both.
/// </summary>
/// <remarks>
/// Search indexes aren't part of a transaction: a migration that fails after creating one doesn't remove it.
/// </remarks>
public static class SearchIndexManagerExtensions
{
    /// <summary>
    /// Creates the index named <paramref name="name"/>, or updates it when it's defined otherwise, comparing the definitions
    /// as the server lists them, with its defaults. It doesn't wait for the index to be built: see
    /// <see cref="WaitUntilQueryableAsync"/>. The collection must exist.
    /// </summary>
    /// <param name="indexes">The collection's search indexes.</param>
    /// <param name="name">The index's name, such as <c>default</c>.</param>
    /// <param name="definition">The definition, as MongoDB Search takes it.</param>
    /// <param name="type">Whether it's a search or a vector search index.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <returns>Whether it was created, updated or left as it was.</returns>
    /// <exception cref="InvalidOperationException">An index of the name is of the other type, which can't be changed.</exception>
    public static async Task<IndexChange> CreateOrUpdateAsync(this IMongoSearchIndexManager indexes,
        string name,
        BsonDocument definition,
        SearchIndexType type = SearchIndexType.Search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(definition);

        var listed = await SearchIndexSync.ListAsync(indexes, name, cancellationToken);
        return await SearchIndexSync.CreateOrUpdateAsync(indexes, name, definition, type, listed, cancellationToken);
    }

    /// <summary>
    /// Waits until the index named <paramref name="name"/> is built and queryable, with its latest definition: one just
    /// updated is searched as it was until then.
    /// </summary>
    /// <param name="indexes">The collection's search indexes.</param>
    /// <param name="name">The index's name.</param>
    /// <param name="timeout">How long to wait at most; as long as it takes without.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <exception cref="InvalidOperationException">The index failed to build, or there's none of the name.</exception>
    /// <exception cref="TimeoutException">It wasn't queryable within <paramref name="timeout"/>.</exception>
    public static async Task WaitUntilQueryableAsync(this IMongoSearchIndexManager indexes,
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } after)
        {
            limit.CancelAfter(after);
        }

        try
        {
            await SearchIndexSync.WaitAsync(indexes, name, TimeProvider.System, building: null, limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The search index {name} wasn't queryable within {timeout}.");
        }
    }
}
