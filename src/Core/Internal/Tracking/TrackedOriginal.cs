using MongoDB.Bson;

namespace MongoFlow;

/// <summary>Where an operation's original is: the change at <paramref name="Index"/> of a save's changes.</summary>
internal readonly record struct TrackedOriginal(TrackedChanges Changes,
    int Index)
{
    public RawBsonDocument Read() => Changes.ReadOriginal(Index);
}
