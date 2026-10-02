namespace MongoFlow;

/// <summary>Where an element of a BSON document lies in its bytes, for <see cref="BsonDiff"/>.</summary>
internal struct BsonElementRange(int nameStart,
    int nameLength,
    byte type,
    int valueStart,
    int end)
{
    public readonly byte Type => type;

    /// <summary>Where the next element starts.</summary>
    public readonly int End => end;

    /// <summary>Whether the other document has an element with the same name.</summary>
    public bool Matched { get; set; }

    public readonly ReadOnlySpan<byte> Name(ReadOnlySpan<byte> document) => document.Slice(nameStart, nameLength);

    public readonly ReadOnlySpan<byte> Value(ReadOnlySpan<byte> document) => document[valueStart..end];
}
