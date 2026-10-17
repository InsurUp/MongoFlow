using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// What differs between a declared index or collection and the server's: what MongoDB can't change in place, and the
/// <c>collMod</c> document that changes the rest, or <see langword="null"/> when nothing else differs.
/// </summary>
internal sealed record SpecComparison(IReadOnlyList<string> Conflicts,
    BsonDocument? Changes);
