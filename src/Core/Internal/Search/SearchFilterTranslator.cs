using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Translates a rendered query filter into <c>$search</c> clauses, for the fields its index maps to compare, leaving the
/// rest to match after the search. Values are copied as rendered, so a member's serializer decides them as it does for
/// any filter.
/// </summary>
/// <remarks>
/// Every clause is positive: it matches exactly the documents its part of the filter matches. A negation is a compound of
/// <c>mustNot</c> alone, which matches every document the negated clause doesn't. A null compared for equality matches an
/// explicit null and a missing field, as in MQL, which takes <c>equals null</c> or no <c>exists</c>.
/// </remarks>
internal static class SearchFilterTranslator
{
    /// <summary>Splits <paramref name="filter"/> into what <paramref name="mapping"/> filters on and what it doesn't.</summary>
    public static SearchFilterSplit Split(BsonDocument filter,
        SearchIndexMapping mapping)
    {
        var clauses = new List<BsonDocument>();
        var mustNot = new List<BsonDocument>();
        var residual = new List<BsonDocument>();
        var postFiltered = new List<string>();

        foreach (var conjunct in QueryFilterParts.Conjuncts(filter))
        {
            var field = conjunct.GetElement(0);
            if (field.Name is [not '$', ..] && field.Value is BsonDocument operators && QueryFilterParts.IsOperators(operators))
            {
                // A field's operators are each a condition of their own, so those the index can't filter on stay behind alone.
                var left = new BsonDocument();
                foreach (var condition in operators)
                {
                    if (Operator(mapping, field.Name, condition.Name, condition.Value) is { } clause)
                    {
                        Add(clause, clauses, mustNot);
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

            if (Positive(mapping, conjunct) is { } translated)
            {
                Add(translated, clauses, mustNot);
            }
            else
            {
                residual.Add(conjunct);
                QueryFilterParts.AddFields(conjunct, postFiltered);
            }
        }

        return new SearchFilterSplit(clauses, mustNot, FilterDocuments.And([.. residual]), postFiltered);
    }

    // A clause matching exactly what a filter document matches, or null when part of it can't be translated.
    private static BsonDocument? Positive(SearchIndexMapping mapping,
        BsonDocument filter)
    {
        var clauses = new List<BsonDocument>();

        foreach (var conjunct in QueryFilterParts.Conjuncts(filter))
        {
            var element = conjunct.GetElement(0);
            var clause = element.Name switch
            {
                "$or" => Any(mapping, element.Value),
                "$nor" => Any(mapping, element.Value) is { } any ? Not(any) : null,
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

        return And(clauses);
    }

    // A should of each branch's clause, when every branch translates.
    private static BsonDocument? Any(SearchIndexMapping mapping,
        BsonValue branches)
    {
        if (branches is not BsonArray { Count: > 0 } array || !array.All(branch => branch is BsonDocument))
        {
            return null;
        }

        var should = new BsonArray();
        foreach (var branch in array.Cast<BsonDocument>())
        {
            if (Positive(mapping, branch) is not { } clause)
            {
                return null;
            }

            should.Add(clause);
        }

        return should.Count == 1
            ? should[0].AsBsonDocument
            : new BsonDocument("compound", new BsonDocument { { "should", should }, { "minimumShouldMatch", 1 } });
    }

    // Every operator of one field, when each translates.
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

        return And(clauses);
    }

    // One operator on one field, or null when the index can't filter on it as MQL does.
    private static BsonDocument? Operator(SearchIndexMapping mapping,
        string path,
        string name,
        BsonValue value) =>
        name switch
        {
            "$eq" => Equal(mapping, path, value),
            "$ne" => Equal(mapping, path, value) is { } equal ? Not(equal) : null,
            "$in" => In(mapping, path, value),
            "$nin" => In(mapping, path, value) is { } any ? Not(any) : null,
            "$gt" or "$gte" or "$lt" or "$lte" when mapping.CanRange(path, value) =>
                new BsonDocument("range", new BsonDocument { { "path", path }, { name[1..], value } }),
            "$exists" when value is BsonBoolean or BsonInt32 or BsonInt64 or BsonDouble && mapping.CanCompareNull(path) =>
                value.ToBoolean() ? Exists(path) : Not(Exists(path)),
            "$not" => value is BsonDocument negated && QueryFilterParts.IsOperators(negated) && All(mapping, path, negated) is { } all ? Not(all) : null,
            _ => null
        };

    private static BsonDocument? Equal(SearchIndexMapping mapping,
        string path,
        BsonValue value)
    {
        if (!mapping.CanEqual(path, value))
        {
            return null;
        }

        var equals = new BsonDocument("equals", new BsonDocument { { "path", path }, { "value", value } });

        // MQL's null matches a missing field too, which equals null doesn't.
        return value is BsonNull
            ? new BsonDocument("compound", new BsonDocument
            {
                { "should", new BsonArray { equals, Not(Exists(path)) } },
                { "minimumShouldMatch", 1 }
            })
            : equals;
    }

    // $in as one in clause when its values are of one kind and not null, else a should of an equals for each.
    private static BsonDocument? In(SearchIndexMapping mapping,
        string path,
        BsonValue value)
    {
        if (value is not BsonArray { Count: > 0 } values || values.Any(item => item is BsonArray or BsonDocument or BsonRegularExpression))
        {
            return null;
        }

        var kinds = values.Select(Kind).Distinct().Count();
        if (kinds == 1 && values[0] is not BsonNull && values.All(item => mapping.CanEqual(path, item)))
        {
            return new BsonDocument("in", new BsonDocument { { "path", path }, { "value", values.DeepClone() } });
        }

        var should = new BsonArray();
        foreach (var item in values)
        {
            if (Equal(mapping, path, item) is not { } equal)
            {
                return null;
            }

            should.Add(equal);
        }

        return new BsonDocument("compound", new BsonDocument { { "should", should }, { "minimumShouldMatch", 1 } });
    }

    private static BsonDocument Exists(string path) => new("exists", new BsonDocument("path", path));

    private static BsonDocument Not(BsonDocument clause) =>
        new("compound", new BsonDocument("mustNot", new BsonArray { clause }));

    private static BsonDocument? And(List<BsonDocument> clauses) =>
        clauses.Count switch
        {
            0 => null,
            1 => clauses[0],
            _ => new BsonDocument("compound", new BsonDocument("filter", new BsonArray(clauses)))
        };

    // A negation goes to mustNot as what it negates, which reads as the filter was written.
    private static void Add(BsonDocument clause,
        List<BsonDocument> filter,
        List<BsonDocument> mustNot)
    {
        if (clause.GetValue("compound", BsonNull.Value) is BsonDocument { ElementCount: 1 } compound &&
            compound.GetValue("mustNot", BsonNull.Value) is BsonArray { Count: 1 } negated)
        {
            mustNot.Add(negated[0].AsBsonDocument);
        }
        else
        {
            filter.Add(clause);
        }
    }

    // Numbers compare with each other whatever their BSON type; in takes values of one kind.
    private static string Kind(BsonValue value) =>
        value switch
        {
            BsonInt32 or BsonInt64 or BsonDouble => "number",
            _ => value.BsonType.ToString()
        };
}
