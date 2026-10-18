using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Whether a search index is defined as declared, comparing the declaration with the definition the server lists, which
/// adds its defaults, may order things its own way and, on self-managed mongot, lists the index's own metadata at its root.
/// </summary>
internal static class SearchIndexDefinitions
{
    // What a definition can say. An element the server lists that the declaration doesn't have is a difference when it's one
    // of these, as one a declaration no longer sets; any other is a default a newer server adds, which isn't.
    private static readonly HashSet<string> Options =
    [
        "analyzer", "analyzers", "dynamic", "fields", "foldDiacritics", "hnswOptions", "ignoreAbove", "indexDoubles",
        "indexIntegers", "indexOptions", "indexingMethod", "mappings", "maxGrams", "minGrams", "multi", "name", "normalizer",
        "norms", "numDimensions", "numPartitions", "path", "quantization", "representation", "searchAnalyzer", "similarity",
        "store", "storedSource", "synonyms", "tokenization", "type", "typeSet", "typeSets", "types"
    ];

    /// <summary>Whether <paramref name="listed"/>, as the server lists it, defines what <paramref name="declared"/> does.</summary>
    public static bool Matches(BsonDocument declared,
        BsonDocument listed,
        SearchIndexType type) =>
        Same(Root(type == SearchIndexType.Search ? WithDefaults(declared) : declared), Root(listed));

    /// <summary>A search index definition with the defaults the server fills in, where it doesn't set them.</summary>
    public static BsonDocument WithDefaults(BsonDocument definition)
    {
        var filled = (BsonDocument)definition.DeepClone();
        if (filled.GetValue("mappings", BsonNull.Value) is BsonDocument mappings)
        {
            FillDocument(mappings, root: true);
        }

        if (filled.GetValue("typeSets", BsonNull.Value) is BsonArray typeSets)
        {
            foreach (var type in typeSets.OfType<BsonDocument>()
                         .SelectMany(typeSet => typeSet.GetValue("types", BsonNull.Value) as BsonArray ?? [])
                         .OfType<BsonDocument>())
            {
                FillMapping(type);
            }
        }

        return filled;
    }

    // A definition's root as compared: without the index's name and type, which self-managed mongot lists there with the
    // rest of the index's metadata, as neither is part of its definition, and with the one partition the server defaults to.
    private static BsonDocument Root(BsonDocument definition)
    {
        var root = new BsonDocument(definition.Where(element => element.Name is not ("name" or "type")));
        Default(root, "numPartitions", 1);
        return root;
    }

    private static void FillDocument(BsonDocument document,
        bool root)
    {
        if (!root)
        {
            Default(document, "dynamic", false);
        }

        if (!document.Contains("fields"))
        {
            document["fields"] = new BsonDocument();
        }

        if (document["fields"] is not BsonDocument fields)
        {
            return;
        }

        foreach (var field in fields)
        {
            var mappings = field.Value switch
            {
                BsonDocument one => [one],
                BsonArray many => many.OfType<BsonDocument>(),
                _ => []
            };

            foreach (var mapping in mappings)
            {
                FillMapping(mapping);
            }
        }
    }

    private static void FillMapping(BsonDocument mapping)
    {
        switch (mapping.GetValue("type", BsonNull.Value).ToString())
        {
            case "string":
                Default(mapping, "indexOptions", "offsets");
                Default(mapping, "store", true);
                Default(mapping, "norms", "include");
                if (mapping.GetValue("multi", BsonNull.Value) is BsonDocument multi)
                {
                    foreach (var child in multi.Values.OfType<BsonDocument>())
                    {
                        FillMapping(child);
                    }
                }

                break;

            case "autocomplete":
                Default(mapping, "minGrams", 2);
                Default(mapping, "maxGrams", 15);
                Default(mapping, "foldDiacritics", true);
                Default(mapping, "tokenization", "edgeGram");
                break;

            case "number":
                Default(mapping, "representation", "double");
                Default(mapping, "indexDoubles", true);
                Default(mapping, "indexIntegers", true);
                break;

            case "document" or "embeddedDocuments":
                FillDocument(mapping, root: false);
                break;
        }
    }

    private static void Default(BsonDocument document,
        string name,
        BsonValue value)
    {
        if (!document.Contains(name))
        {
            document[name] = value;
        }
    }

    // Whether actual says what expected does: the same elements in any order, numbers of any type, arrays in any order.
    private static bool Same(BsonValue expected,
        BsonValue actual)
    {
        if (expected.IsNumeric && actual.IsNumeric)
        {
            return expected.ToDouble() == actual.ToDouble();
        }

        switch (expected, actual)
        {
            case (BsonDocument left, BsonDocument right):
                foreach (var element in left)
                {
                    if (!right.TryGetValue(element.Name, out var value) ||
                        !(element is { Name: "fields" or "multi", Value: BsonDocument fields } && value is BsonDocument listed
                            ? SameFields(fields, listed)
                            : Same(element.Value, value)))
                    {
                        return false;
                    }
                }

                return right.Names.All(name => left.Contains(name) || !Options.Contains(name));

            case (BsonArray left, BsonArray right):
                if (left.Count != right.Count)
                {
                    return false;
                }

                var unmatched = right.ToList();
                foreach (var item in left)
                {
                    var match = unmatched.FindIndex(candidate => Same(item, candidate));
                    if (match < 0)
                    {
                        return false;
                    }

                    unmatched.RemoveAt(match);
                }

                return true;

            default:
                return expected.Equals(actual);
        }
    }

    // Fields by name, each mapped as declared: one the declaration no longer has is a difference, and a field mapped one way
    // is the same as an array of that one mapping.
    private static bool SameFields(BsonDocument expected,
        BsonDocument actual) =>
        expected.ElementCount == actual.ElementCount &&
        expected.All(field => actual.TryGetValue(field.Name, out var listed) && Same(Mappings(field.Value), Mappings(listed)));

    private static BsonArray Mappings(BsonValue mapping) => mapping as BsonArray ?? [mapping];
}
