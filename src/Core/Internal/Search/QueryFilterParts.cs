using MongoDB.Bson;

namespace MongoFlow;

/// <summary>The parts of a rendered query filter the search translators take apart.</summary>
internal static class QueryFilterParts
{
    /// <summary>The top-level conditions of a filter, each a document of one element, with <c>$and</c> flattened.</summary>
    public static IEnumerable<BsonDocument> Conjuncts(BsonDocument filter)
    {
        foreach (var element in filter)
        {
            if (element is { Name: "$and", Value: BsonArray all } && all.All(item => item is BsonDocument))
            {
                foreach (var conjunct in all.Cast<BsonDocument>().SelectMany(Conjuncts))
                {
                    yield return conjunct;
                }
            }
            else
            {
                yield return new BsonDocument(element);
            }
        }
    }

    /// <summary>Whether a field's value is a document of operators, such as <c>{ $gt: 1 }</c>, rather than one to equal.</summary>
    public static bool IsOperators(BsonDocument value) => value.ElementCount > 0 && value.Names.All(name => name.StartsWith('$'));

    /// <summary>
    /// Adds the fields <paramref name="condition"/> compares to <paramref name="fields"/>, once each: those named at its top
    /// level, and under <c>$and</c>, <c>$or</c> and <c>$nor</c>; any other operator, such as <c>$expr</c>, by its name.
    /// </summary>
    public static void AddFields(BsonDocument condition,
        List<string> fields)
    {
        foreach (var element in condition)
        {
            if (element is { Name: "$and" or "$or" or "$nor", Value: BsonArray branches })
            {
                foreach (var branch in branches.OfType<BsonDocument>())
                {
                    AddFields(branch, fields);
                }
            }
            else if (!fields.Contains(element.Name))
            {
                fields.Add(element.Name);
            }
        }
    }
}
