namespace MongoFlow;

/// <summary>What setting up an index did to it: a search index, or a regular one.</summary>
public enum IndexChange
{
    /// <summary>It was defined as declared already.</summary>
    Unchanged,

    /// <summary>There was none of its name, so it was created; a search index is queryable once built.</summary>
    Created,

    /// <summary>
    /// It was defined otherwise, so it was changed to match: a search index is rebuilt, and searched as it was meanwhile; a
    /// regular index has its expiry, or whether it's hidden, changed in place.
    /// </summary>
    Updated
}
