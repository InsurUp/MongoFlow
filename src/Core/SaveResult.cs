namespace MongoFlow;

/// <summary>What a save changed, summed over its operations.</summary>
public readonly record struct SaveResult(long Inserted, long Matched, long Modified, long Deleted)
{
    public static SaveResult Empty => default;
}
