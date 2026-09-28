namespace MongoFlow;

/// <summary>What a save changed, summed over its operations.</summary>
public sealed record SaveResult(long Inserted, long Matched, long Modified, long Deleted)
{
    public static SaveResult Empty { get; } = new(0, 0, 0, 0);
}
