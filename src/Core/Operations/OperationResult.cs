namespace MongoFlow;

/// <summary>What one operation changed, from the bulk write's per-operation result.</summary>
public sealed record OperationResult(long Inserted, long Matched, long Modified, long Deleted)
{
    // A single-document write only ever inserts, matches, modifies or deletes 0 or 1 documents, so those results are
    // shared instead of allocated for every operation.
    private static readonly OperationResult[] Shared =
        [.. Enumerable.Range(0, 16).Select(i => new OperationResult(i >> 3 & 1, i >> 2 & 1, i >> 1 & 1, i & 1))];

    internal static OperationResult Of(long inserted,
        long matched,
        long modified,
        long deleted) =>
        (inserted | matched | modified | deleted) is 0 or 1
            ? Shared[(int)(inserted << 3 | matched << 2 | modified << 1 | deleted)]
            : new OperationResult(inserted, matched, modified, deleted);
}
