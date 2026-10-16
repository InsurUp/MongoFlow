using MongoDB.Bson;

namespace MongoFlow;

/// <summary>How a search's own operator and a query filter's clauses are put together.</summary>
internal static class SearchStages
{
    /// <summary>
    /// <paramref name="search"/>, a rendered operator or collector, filtered with <paramref name="split"/>'s clauses: as the
    /// one <c>must</c> of a compound beside them, whose score is the operator's, or for a <c>facet</c> collector, in its
    /// operator. The caller's compound is never merged into, where its <c>should</c> would become optional.
    /// </summary>
    public static BsonDocument Filter(BsonDocument search,
        SearchFilterSplit split)
    {
        if (!split.HasClauses)
        {
            return search;
        }

        if (IsFacet(search, out var facet))
        {
            var filtered = (BsonDocument)facet.DeepClone();
            filtered["operator"] = facet.GetValue("operator", BsonNull.Value) is BsonDocument inner
                ? Compound(inner, split)
                : Compound(null, split);

            return new BsonDocument("facet", filtered);
        }

        return Compound(search, split);
    }

    /// <summary>Whether <paramref name="search"/> is a <c>facet</c> collector, whose facets count what it matches.</summary>
    public static bool IsFacet(BsonDocument search,
        out BsonDocument facet)
    {
        facet = search.GetValue("facet", BsonNull.Value) as BsonDocument ?? [];
        return search.ElementCount == 1 && search.Contains("facet");
    }

    private static BsonDocument Compound(BsonDocument? must,
        SearchFilterSplit split)
    {
        var compound = new BsonDocument();
        if (must is not null)
        {
            compound["must"] = new BsonArray { must };
        }

        if (split.Filter.Count > 0)
        {
            compound["filter"] = new BsonArray(split.Filter.Select(clause => clause.DeepClone()));
        }

        if (split.MustNot.Count > 0)
        {
            compound["mustNot"] = new BsonArray(split.MustNot.Select(clause => clause.DeepClone()));
        }

        return new BsonDocument("compound", compound);
    }
}
