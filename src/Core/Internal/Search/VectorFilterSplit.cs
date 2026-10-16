using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// A query filter split for a vector search: the filter the search applies, in its index, and what's left to match after it.
/// </summary>
/// <param name="Filter">The MQL filter for <c>$vectorSearch</c>, on the index's filter fields; <see langword="null"/> when none.</param>
/// <param name="Residual">What the index can't filter on, matched after the search; <see langword="null"/> when nothing is.</param>
/// <param name="PostFiltered">The fields <paramref name="Residual"/> compares, which the index has no filter field for.</param>
internal sealed record VectorFilterSplit(BsonDocument? Filter,
    BsonDocument? Residual,
    IReadOnlyList<string> PostFiltered)
{
    /// <summary>Nothing to filter by.</summary>
    public static VectorFilterSplit None { get; } = new(null, null, []);
}
