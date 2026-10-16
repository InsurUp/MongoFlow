using MongoDB.Bson;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>
/// A collection's query filters as a search applies them, for a pipeline built by hand: the clauses its index filters on,
/// and what's left to match after the search stage. <c>SearchAsync</c> applies them the same way.
/// </summary>
/// <remarks>
/// A part of the query filters is translated into a clause only for a field the index maps to compare it exactly: a
/// <c>token</c> for strings, <c>boolean</c>, <c>number</c>, <c>date</c>, <c>objectId</c> or <c>uuid</c>. The rest stays in
/// <see cref="PostFilter"/>, which must follow the search stage as a <c>$match</c>.
/// </remarks>
public sealed class SearchFilters<TDocument>
{
    private readonly SearchFilterSplit _split;

    internal SearchFilters(SearchFilterSplit split,
        bool matchesNothing)
    {
        _split = split;
        MatchesNothing = matchesNothing;
    }

    /// <summary>Clauses each document must match, for a compound's <c>filter</c>. They don't score.</summary>
    public IReadOnlyList<BsonDocument> Filter => _split.Filter;

    /// <summary>Clauses no document may match, for a compound's <c>mustNot</c>.</summary>
    public IReadOnlyList<BsonDocument> MustNot => _split.MustNot;

    /// <summary>
    /// What the index can't filter on, for a <c>$match</c> right after the search stage; <see langword="null"/> when the
    /// clauses filter everything.
    /// </summary>
    public BsonDocument? PostFilter => _split.Residual;

    /// <summary>
    /// Whether the query filters match nothing, such as a per-query filter returning <c>_ =&gt; false</c>: what's searched
    /// is all left to <see cref="PostFilter"/>, which matches nothing.
    /// </summary>
    public bool MatchesNothing { get; }

    /// <summary>
    /// Whether what the search counts, facets and returns as stored source is what the query filters show: only when
    /// nothing is left to <see cref="PostFilter"/>.
    /// </summary>
    public bool CanCount => PostFilter is null;

    /// <summary>
    /// <paramref name="search"/> filtered with <see cref="Filter"/> and <see cref="MustNot"/>: as the one <c>must</c> of a
    /// compound beside them, whose score is its own, or for a <c>facet</c> collector, in its operator.
    /// <see cref="PostFilter"/> still has to follow the stage.
    /// </summary>
    public SearchDefinition<TDocument> Apply(SearchDefinition<TDocument> search)
    {
        ArgumentNullException.ThrowIfNull(search);

        return _split.HasClauses ? new FilteredSearchDefinition<TDocument>(search, _split) : search;
    }
}
