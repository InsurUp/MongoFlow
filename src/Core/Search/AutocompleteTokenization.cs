namespace MongoFlow;

/// <summary>How an autocomplete mapping cuts a value into the pieces a query's prefix is matched with.</summary>
public enum AutocompleteTokenization
{
    /// <summary>From the start of each word: <c>ada</c> is matched by <c>a</c>, <c>ad</c> and <c>ada</c>.</summary>
    EdgeGram,

    /// <summary>From the end of each word.</summary>
    RightEdgeGram,

    /// <summary>Every run of letters inside each word, for matches in the middle of words, at the cost of a larger index.</summary>
    NGram
}
