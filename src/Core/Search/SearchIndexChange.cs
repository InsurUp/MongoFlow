namespace MongoFlow;

/// <summary>What <c>CreateOrUpdateAsync</c> did to a search index.</summary>
public enum SearchIndexChange
{
    /// <summary>It was defined as given already.</summary>
    Unchanged,

    /// <summary>There was none of its name, so it was created; it's queryable once built.</summary>
    Created,

    /// <summary>It was defined otherwise, so it was updated; it's searched as it was until it's rebuilt.</summary>
    Updated
}
