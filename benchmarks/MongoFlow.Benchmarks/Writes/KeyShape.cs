namespace MongoFlow.Benchmarks;

/// <summary>The shapes of key a write by key matches.</summary>
public enum KeyShape
{
    /// <summary>The member mapped to <c>_id</c>.</summary>
    Id,

    /// <summary>Two members together, under element names of their own.</summary>
    Composite
}
