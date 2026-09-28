namespace MongoFlow;

/// <summary>What one operation changed, from the bulk write's per-operation result.</summary>
public sealed record OperationResult(long Inserted, long Matched, long Modified, long Deleted);
