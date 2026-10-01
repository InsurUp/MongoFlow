namespace MongoFlow;

/// <summary>A field every read of a collection filters on, for the feature that filters on it, which an index should include.</summary>
internal sealed record IndexedField(string Field, string Feature);
