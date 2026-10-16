using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// What a search or vector search index maps, read from its definition: which fields a search's filters can compare
/// exactly, and how. A clause on a field the index doesn't map matches nothing, and negated matches everything, so a
/// query filter's clause is only translated for a field mapped to compare it.
/// </summary>
internal sealed class SearchIndexMapping
{
    // Integers beyond 2^53 lose precision as doubles, which a number mapping indexes them as unless it says int64.
    private const long ExactDoubles = 1L << 53;

    // The types whose mappings compare values exactly: equals, in and, but for booleans and uuids, range.
    private static readonly HashSet<string> ExactTypes = ["token", "boolean", "objectId", "number", "date", "uuid"];

    // What dynamic mappings index each BSON type as, without a type set: strings as analyzed text, which equals can't match.
    private static readonly SearchMappedType[] DynamicTypes =
    [
        new("boolean", []), new("objectId", []), new("number", []), new("date", [])
    ];

    private readonly Dictionary<string, List<SearchMappedType>> _fields;
    private readonly List<(string Prefix, SearchMappedType[] Dynamic)> _documents;
    private readonly HashSet<string> _vectorFilters;

    private SearchIndexMapping(SearchIndexType type,
        Dictionary<string, List<SearchMappedType>> fields,
        List<(string Prefix, SearchMappedType[] Dynamic)> documents,
        HashSet<string> vectorFilters)
    {
        Type = type;
        _fields = fields;
        _documents = [.. documents.OrderByDescending(document => document.Prefix.Length)];
        _vectorFilters = vectorFilters;
    }

    public SearchIndexType Type { get; }

    /// <summary>Reads <paramref name="definition"/>, as created or as <c>$listSearchIndexes</c> shows it.</summary>
    public static SearchIndexMapping Parse(BsonDocument definition,
        SearchIndexType type)
    {
        var fields = new Dictionary<string, List<SearchMappedType>>(StringComparer.Ordinal);
        var documents = new List<(string, SearchMappedType[])>();
        var vectorFilters = new HashSet<string>(StringComparer.Ordinal);

        if (type == SearchIndexType.VectorSearch)
        {
            if (definition.TryGetValue("fields", out var entries) && entries is BsonArray array)
            {
                foreach (var entry in array.OfType<BsonDocument>())
                {
                    if (entry.GetValue("type", BsonNull.Value) == "filter" && entry.GetValue("path", BsonNull.Value) is BsonString path)
                    {
                        vectorFilters.Add(path.Value);
                    }
                }
            }
        }
        else if (definition.TryGetValue("mappings", out var mappings) && mappings is BsonDocument root)
        {
            var typeSets = (definition.GetValue("typeSets", BsonNull.Value) as BsonArray ?? [])
                .OfType<BsonDocument>()
                .Where(typeSet => typeSet.GetValue("name", BsonNull.Value).IsString)
                .ToDictionary(typeSet => typeSet["name"].AsString,
                    typeSet => (typeSet.GetValue("types", BsonNull.Value) as BsonArray ?? [])
                        .OfType<BsonDocument>()
                        .Select(SearchMappedType.From)
                        .OfType<SearchMappedType>()
                        .ToArray());

            ParseDocument(root, "", fields, documents, typeSets);
        }

        return new SearchIndexMapping(type, fields, documents, vectorFilters);
    }

    /// <summary>Whether <c>equals</c> can compare <paramref name="path"/> with <paramref name="value"/> as MQL equality does.</summary>
    public bool CanEqual(string path,
        BsonValue value) =>
        value is BsonNull
            ? CanCompareNull(path)
            : TypesOf(path).Any(type => type.Equates(value));

    /// <summary>Whether <c>range</c> can compare <paramref name="path"/> with <paramref name="value"/> as MQL does.</summary>
    public bool CanRange(string path,
        BsonValue value) =>
        TypesOf(path).Any(type => type.Ranges(value));

    /// <summary>
    /// Whether <c>exists</c> finds the documents holding <paramref name="path"/>, null included, and with
    /// <c>equals null</c>, those where it's null: it needs an exact mapping, explicit, which indexes nulls.
    /// </summary>
    public bool CanCompareNull(string path) =>
        _fields.TryGetValue(path, out var types) && types.Any(type => ExactTypes.Contains(type.Name));

    /// <summary>Whether a vector search's filter can compare <paramref name="path"/>.</summary>
    public bool IsVectorFilter(string path) => _vectorFilters.Contains(path);

    // The types path is indexed as: its explicit mappings, or else what the nearest document mapping holding it indexes
    // dynamically, if anything.
    private IEnumerable<SearchMappedType> TypesOf(string path)
    {
        if (_fields.TryGetValue(path, out var types))
        {
            return types;
        }

        foreach (var (prefix, dynamic) in _documents)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return dynamic;
            }
        }

        return [];
    }

    private static void ParseDocument(BsonDocument document,
        string prefix,
        Dictionary<string, List<SearchMappedType>> fields,
        List<(string, SearchMappedType[])> documents,
        Dictionary<string, SearchMappedType[]> typeSets)
    {
        if (document.GetValue("fields", BsonNull.Value) is BsonDocument children)
        {
            foreach (var child in children)
            {
                var path = prefix + child.Name;
                var mappings = child.Value switch
                {
                    BsonDocument one => [one],
                    BsonArray many => many.OfType<BsonDocument>().ToArray(),
                    _ => []
                };

                foreach (var mapping in mappings)
                {
                    switch (mapping.GetValue("type", BsonNull.Value).ToString())
                    {
                        case "document":
                            ParseDocument(mapping, path + ".", fields, documents, typeSets);
                            break;

                        // Fields inside embedded documents are only reached with embeddedDocument, so they count as unmapped.
                        case "embeddedDocuments":
                            break;

                        default:
                            if (SearchMappedType.From(mapping) is { } type)
                            {
                                if (!fields.TryGetValue(path, out var types))
                                {
                                    fields[path] = types = [];
                                }

                                types.Add(type);
                            }

                            break;
                    }
                }
            }
        }

        var dynamic = document.GetValue("dynamic", false) switch
        {
            BsonBoolean { Value: true } => DynamicTypes,
            BsonDocument typeSet when typeSet.GetValue("typeSet", BsonNull.Value) is BsonString name &&
                                      typeSets.TryGetValue(name.Value, out var types) => types,
            _ => []
        };

        documents.Add((prefix, dynamic));
    }

    /// <summary>One type a field is mapped as, with the options that decide what it compares.</summary>
    private sealed record SearchMappedType(string Name, BsonDocument Options)
    {
        public static SearchMappedType? From(BsonDocument mapping) =>
            mapping.GetValue("type", BsonNull.Value) is BsonString type ? new SearchMappedType(type.Value, mapping) : null;

        public bool Equates(BsonValue value) =>
            (Name, value) switch
            {
                ("token", BsonString) => !Lowercases,
                ("boolean", BsonBoolean) => true,
                ("objectId", BsonObjectId) => true,
                ("number", BsonInt32 or BsonInt64 or BsonDouble) => IndexesNumber(value),
                ("date", BsonDateTime) => true,
                ("uuid", BsonBinaryData { SubType: BsonBinarySubType.UuidStandard }) => true,
                _ => false
            };

        public bool Ranges(BsonValue value) =>
            (Name, value) switch
            {
                ("token", BsonString) => !Lowercases,
                ("objectId", BsonObjectId) => true,
                ("number", BsonInt32 or BsonInt64 or BsonDouble) => IndexesNumber(value),
                ("date", BsonDateTime) => true,
                _ => false
            };

        // A normalizer that lowercases makes equality ignore case, which MQL's doesn't.
        private bool Lowercases => Options.GetValue("normalizer", "none") == "lowercase";

        private bool IndexesNumber(BsonValue value) =>
            value switch
            {
                BsonDouble => Options.GetValue("indexDoubles", true).ToBoolean(),
                BsonInt64 { Value: var number } => Options.GetValue("indexIntegers", true).ToBoolean() &&
                                                   (Options.GetValue("representation", "double") == "int64" ||
                                                    number is >= -ExactDoubles and <= ExactDoubles),
                _ => Options.GetValue("indexIntegers", true).ToBoolean()
            };
    }
}
