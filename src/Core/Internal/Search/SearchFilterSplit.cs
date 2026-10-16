using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// A query filter split for a search: the clauses the search filters with, in its index, and what's left to match after it.
/// </summary>
/// <param name="Filter">Clauses each document must match, for <c>compound.filter</c>.</param>
/// <param name="MustNot">Clauses no document may match, for <c>compound.mustNot</c>.</param>
/// <param name="Residual">What the index can't filter on, matched after the search; <see langword="null"/> when nothing is.</param>
/// <param name="PostFiltered">The fields <paramref name="Residual"/> compares, which the index doesn't map to compare.</param>
internal sealed record SearchFilterSplit(IReadOnlyList<BsonDocument> Filter,
    IReadOnlyList<BsonDocument> MustNot,
    BsonDocument? Residual,
    IReadOnlyList<string> PostFiltered)
{
    /// <summary>Nothing to filter by.</summary>
    public static SearchFilterSplit None { get; } = new([], [], null, []);

    /// <summary>Whether the search filters with clauses of its own.</summary>
    public bool HasClauses => Filter.Count > 0 || MustNot.Count > 0;
}
