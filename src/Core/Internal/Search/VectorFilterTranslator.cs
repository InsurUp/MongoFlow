using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Translates a rendered query filter into a <c>$vectorSearch</c> filter, for the index's filter fields and the operators
/// it takes, leaving the rest to match after the search.
/// </summary>
/// <remarks>
/// A vector search's filter is MQL, but its null matches only an explicit null, so a null compared for equality is written
/// as an explicit null or a missing field, and <c>$ne: null</c> as a field present and not null. <c>$in</c> and
/// <c>$nin</c> holding null, <c>$not</c>, and anything but plain values stay behind.
/// </remarks>
internal static class VectorFilterTranslator
{
    /// <summary>Splits <paramref name="filter"/> into what <paramref name="mapping"/> filters on and what it doesn't.</summary>
    public static VectorFilterSplit Split(BsonDocument filter,
        SearchIndexMapping mapping)
    {
        var translated = new List<BsonDocument>();
        var residual = new List<BsonDocument>();
        var postFiltered = new List<string>();

        foreach (var conjunct in QueryFilterParts.Conjuncts(filter))
        {
            var field = conjunct.GetElement(0);
            if (field.Name is [not '$', ..] && field.Value is BsonDocument operators && QueryFilterParts.IsOperators(operators))
            {
                var left = new BsonDocument();
                foreach (var condition in operators)
                {
                    if (Operator(mapping, field.Name, condition.Name, condition.Value) is { } clause)
                    {
                        translated.Add(clause);
                    }
                    else
                    {
                        left.Add(condition);
                    }
                }

                if (left.ElementCount > 0)
                {
                    var leftover = new BsonDocument(field.Name, left);
                    residual.Add(leftover);
                    QueryFilterParts.AddFields(leftover, postFiltered);
                }

                continue;
            }

            if (Translate(mapping, conjunct) is { } whole)
            {
                translated.Add(whole);
            }
            else
            {
                residual.Add(conjunct);
                QueryFilterParts.AddFields(conjunct, postFiltered);
            }
        }

        return new VectorFilterSplit(Join(translated), FilterDocuments.And([.. residual]), postFiltered);
    }

    // A filter document the vector search matches as MQL would, or null when part of it can't be.
    private static BsonDocument? Translate(SearchIndexMapping mapping,
        BsonDocument filter)
    {
        var clauses = new List<BsonDocument>();

        foreach (var conjunct in QueryFilterParts.Conjuncts(filter))
        {
            var element = conjunct.GetElement(0);
            var clause = element.Name switch
            {
                "$or" or "$nor" => Branches(mapping, element.Name, element.Value),
                ['$', ..] => null,
                _ when element.Value is BsonDocument operators && QueryFilterParts.IsOperators(operators) => All(mapping, element.Name, operators),
                _ => Operator(mapping, element.Name, "$eq", element.Value)
            };

            if (clause is null)
            {
                return null;
            }

            clauses.Add(clause);
        }

        return Join(clauses);
    }

    private static BsonDocument? Branches(SearchIndexMapping mapping,
        string name,
        BsonValue branches)
    {
        if (branches is not BsonArray { Count: > 0 } array || !array.All(branch => branch is BsonDocument))
        {
            return null;
        }

        var translated = new BsonArray();
        foreach (var branch in array.Cast<BsonDocument>())
        {
            if (Translate(mapping, branch) is not { } clause)
            {
                return null;
            }

            translated.Add(clause);
        }

        return new BsonDocument(name, translated);
    }

    private static BsonDocument? All(SearchIndexMapping mapping,
        string path,
        BsonDocument operators)
    {
        var clauses = new List<BsonDocument>();
        foreach (var condition in operators)
        {
            if (Operator(mapping, path, condition.Name, condition.Value) is not { } clause)
            {
                return null;
            }

            clauses.Add(clause);
        }

        return Join(clauses);
    }

    private static BsonDocument? Operator(SearchIndexMapping mapping,
        string path,
        string name,
        BsonValue value)
    {
        if (!mapping.IsVectorFilter(path))
        {
            return null;
        }

        return (name, value) switch
        {
            ("$eq", BsonNull) => new BsonDocument("$or", new BsonArray
            {
                new BsonDocument(path, BsonNull.Value),
                new BsonDocument(path, new BsonDocument("$exists", false))
            }),
            ("$ne", BsonNull) => new BsonDocument("$and", new BsonArray
            {
                new BsonDocument(path, new BsonDocument("$exists", true)),
                new BsonDocument(path, new BsonDocument("$ne", BsonNull.Value))
            }),
            ("$eq" or "$ne" or "$gt" or "$gte" or "$lt" or "$lte", _) when IsPlain(value) =>
                new BsonDocument(path, new BsonDocument(name, value)),
            ("$in" or "$nin", BsonArray { Count: > 0 } values) when values.All(IsPlain) =>
                new BsonDocument(path, new BsonDocument(name, values.DeepClone())),
            ("$exists", BsonBoolean or BsonInt32 or BsonInt64 or BsonDouble) =>
                new BsonDocument(path, new BsonDocument("$exists", value.ToBoolean())),
            _ => null
        };
    }

    // The values a vector search's filter compares: booleans, dates, ObjectIds, numbers, strings and UUIDs.
    private static bool IsPlain(BsonValue value) =>
        value is BsonBoolean or BsonDateTime or BsonObjectId or BsonInt32 or BsonInt64 or BsonDouble or BsonString ||
        value is BsonBinaryData { SubType: BsonBinarySubType.UuidStandard };

    // Clauses side by side when their fields differ, or under $and.
    private static BsonDocument? Join(List<BsonDocument> clauses) =>
        clauses.Count == 0 ? null : FilterDocuments.And([.. clauses]);
}
