using System.Globalization;
using MongoDB.Bson;

namespace MongoFlow;

/// <summary>The names the server, and the driver, give an index that isn't named.</summary>
internal static class IndexNaming
{
    /// <summary>
    /// Each key's field and value, joined by underscores, such as <c>Customer_1_PlacedAt_-1</c> or <c>Name_text</c>, as the
    /// driver names an index it creates without a name. Numbers are written in the invariant culture, where the driver uses
    /// the current one.
    /// </summary>
    public static string Of(BsonDocument keys) =>
        string.Join('_', keys.Elements.Select(key => $"{key.Name.Replace(' ', '_')}_{Value(key.Value)}"));

    private static string Value(BsonValue value) =>
        value.BsonType switch
        {
            BsonType.Int32 or BsonType.Int64 or BsonType.Double => value.ToInt32().ToString(CultureInfo.InvariantCulture),
            BsonType.String => value.AsString.Replace(' ', '_'),
            _ => "x"
        };
}
