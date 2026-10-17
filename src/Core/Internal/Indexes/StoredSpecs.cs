using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Compares what a collection or index declares with what the server lists of it.</summary>
internal static class StoredSpecs
{
    // What the server fills in for a collation that doesn't set them, whatever its locale; the rest depend on the locale.
    private static readonly BsonDocument CollationDefaults = new()
    {
        { "strength", 3 },
        { "caseLevel", false },
        { "numericOrdering", false },
        { "alternate", "non-ignorable" }
    };

    /// <summary>
    /// Whether two values are the same as the server compares filters and keys: documents with the same elements in any
    /// order, arrays with the same items in order, and numbers of any type that are equal. A missing value is a null one.
    /// </summary>
    public static bool Same(BsonValue? left,
        BsonValue? right)
    {
        left ??= BsonNull.Value;
        right ??= BsonNull.Value;

        return (left, right) switch
        {
            (BsonInt32 or BsonInt64, BsonInt32 or BsonInt64) => left.ToInt64() == right.ToInt64(),
            ({ IsNumeric: true }, { IsNumeric: true }) => left.ToDouble().Equals(right.ToDouble()),
            (BsonDocument a, BsonDocument b) => a.ElementCount == b.ElementCount &&
                                                a.Elements.All(element => b.TryGetValue(element.Name, out var other) &&
                                                                          Same(element.Value, other)),
            (BsonArray a, BsonArray b) => a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second)),
            _ => left.Equals(right)
        };
    }

    /// <summary>Whether two index keys are the same: the same fields, in the same order, with the same values.</summary>
    public static bool SameKeys(BsonDocument left,
        BsonDocument right) =>
        left.ElementCount == right.ElementCount &&
        left.Elements.Zip(right.Elements).All(pair => pair.First.Name == pair.Second.Name && Same(pair.First.Value, pair.Second.Value));

    /// <summary>
    /// <paramref name="collation"/> as the server stores it, or <see langword="null"/> for none, which the simple collation
    /// is.
    /// </summary>
    public static BsonDocument? Collation(Collation? collation) => Stored(collation?.ToBsonDocument());

    /// <summary>
    /// Whether <paramref name="stored"/>, a collation the server lists, is <paramref name="expected"/>: the same locale, and
    /// each option <paramref name="expected"/> sets the same; of the options it doesn't set, those whose default doesn't
    /// depend on the locale at their default. Either is <see langword="null"/> for none.
    /// </summary>
    public static bool SameCollation(BsonDocument? expected,
        BsonDocument? stored)
    {
        (expected, stored) = (Stored(expected), Stored(stored));
        if (expected is null || stored is null)
        {
            return expected is null && stored is null;
        }

        return expected.Elements.Where(element => element.Name != "version")
                   .All(element => Same(element.Value, stored.GetValue(element.Name, BsonNull.Value))) &&
               CollationDefaults.Elements.Where(element => !expected.Contains(element.Name))
                   .All(element => !stored.TryGetValue(element.Name, out var value) || Same(value, element.Value));
    }

    /// <summary>How a collation is shown in a message: <c>simple</c> for none.</summary>
    public static string Describe(BsonDocument? collation) => Stored(collation)?.ToJson() ?? "simple";

    // The simple collation is none: the server lists an index of it without one.
    private static BsonDocument? Stored(BsonDocument? collation) =>
        collation is null || collation.GetValue("locale", BsonNull.Value) is BsonString { Value: "simple" } ? null : collation;
}
