using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A search index a collection declares, rendered once the collection's serializers are known.</summary>
internal sealed class SearchIndexBuilder<TDocument>(string name) : ISearchIndexBuilder<TDocument>
{
    private readonly List<(LambdaExpression? Member, string? Path, SearchField[] Fields)> _maps = [];
    private BsonDocument? _definition;
    private string? _analyzer;
    private string? _searchAnalyzer;
    private bool? _dynamic;
    private bool? _documentsDynamic;

    public string Name { get; } = name;

    public ISearchIndexBuilder<TDocument> Definition(BsonDocument definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = (BsonDocument)definition.DeepClone();
        return this;
    }

    public ISearchIndexBuilder<TDocument> Analyzer(string analyzer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);

        _analyzer = analyzer;
        return this;
    }

    public ISearchIndexBuilder<TDocument> SearchAnalyzer(string analyzer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);

        _searchAnalyzer = analyzer;
        return this;
    }

    public ISearchIndexBuilder<TDocument> Dynamic(bool dynamic = true)
    {
        _dynamic = dynamic;
        return this;
    }

    public ISearchIndexBuilder<TDocument> DocumentsDynamic(bool dynamic = true)
    {
        _documentsDynamic = dynamic;
        return this;
    }

    public ISearchIndexBuilder<TDocument> Map<TMember>(Expression<Func<TDocument, TMember>> member,
        params SearchField[] fields)
    {
        ArgumentNullException.ThrowIfNull(member);
        ThrowIfNoFields(fields);

        _maps.Add((member, null, fields));
        return this;
    }

    public ISearchIndexBuilder<TDocument> Map(string path,
        params SearchField[] fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfNoFields(fields);

        _maps.Add((null, path, fields));
        return this;
    }

    /// <summary>
    /// The definition: the one given, with what was declared added, and the fields <paramref name="features"/> filter on
    /// mapped as their values compare, unless they're mapped so already.
    /// </summary>
    public SearchIndexDeclaration Build(RenderArgs<TDocument> args,
        IReadOnlyList<FeatureField> features)
    {
        var definition = _definition is null ? new BsonDocument() : (BsonDocument)_definition.DeepClone();
        if (_analyzer is not null)
        {
            definition["analyzer"] = _analyzer;
        }

        if (_searchAnalyzer is not null)
        {
            definition["searchAnalyzer"] = _searchAnalyzer;
        }

        if (definition.GetValue("mappings", BsonNull.Value) is not BsonDocument mappings)
        {
            definition["mappings"] = mappings = [];
        }

        if (_dynamic is { } dynamic)
        {
            mappings["dynamic"] = dynamic;
        }
        else if (!mappings.Contains("dynamic"))
        {
            mappings["dynamic"] = false;
        }

        var documentsDynamic = _documentsDynamic is { } documents ? (BsonValue)documents : mappings["dynamic"];
        foreach (var (member, path, fields) in _maps)
        {
            var fieldPath = member is null ? path! : FilterDocuments.RenderField(member, args).FieldName;
            var target = Leaf(mappings, fieldPath, documentsDynamic);
            foreach (var field in fields)
            {
                Merge(target, field.Definition);
            }
        }

        foreach (var feature in features)
        {
            if (feature.SearchType is not { } type || IndexedDynamically(mappings, feature))
            {
                continue;
            }

            var target = Leaf(mappings, feature.Path, documentsDynamic);
            if (!target.Mappings.Any(mapping => mapping.GetValue("type", BsonNull.Value) == type))
            {
                Merge(target, new BsonDocument("type", type));
            }
        }

        return new SearchIndexDeclaration(Name, SearchIndexType.Search, definition);
    }

    // Whether the index indexes a feature's field as the feature compares it already, without a mapping: a feature's field is
    // at the root, which, dynamic, indexes a boolean, number, date or ObjectId it doesn't map by its type. One the feature
    // compares with null is mapped still, as a search's filters compare null only on a field that's mapped.
    private static bool IndexedDynamically(BsonDocument mappings,
        FeatureField feature) =>
        feature is { ComparesNull: false, SearchType: "boolean" or "number" or "date" or "objectId" } &&
        mappings["dynamic"] is BsonBoolean { Value: true } &&
        (mappings.GetValue("fields", BsonNull.Value) as BsonDocument)?.Contains(feature.Path) != true;

    // The mappings of the field at path, with a document mapping for each document holding it that isn't mapped already,
    // created with dynamic: by default the index's setting, as the field's documents were indexed before.
    private static (BsonDocument Parent, string Name, List<BsonDocument> Mappings) Leaf(BsonDocument mappings,
        string path,
        BsonValue dynamic)
    {
        var parent = mappings;
        var segments = path.Split('.');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var fields = Fields(parent);
            var existing = MappingsOf(fields, segments[i]);
            var document = existing.FirstOrDefault(mapping => mapping.GetValue("type", BsonNull.Value) == "document");
            if (document is null)
            {
                document = new BsonDocument { { "type", "document" }, { "dynamic", dynamic.DeepClone() } };
                Store(fields, segments[i], [.. existing, document]);
            }

            parent = document;
        }

        return (parent, segments[^1], MappingsOf(Fields(parent), segments[^1]));
    }

    // Adds a mapping to a field's: one of the same type, declared again, takes the earlier one's place.
    private static void Merge((BsonDocument Parent, string Name, List<BsonDocument> Mappings) target,
        BsonDocument mapping)
    {
        var type = mapping["type"];
        var index = target.Mappings.FindIndex(existing => existing.GetValue("type", BsonNull.Value) == type);
        if (index >= 0)
        {
            target.Mappings[index] = (BsonDocument)mapping.DeepClone();
        }
        else
        {
            target.Mappings.Add((BsonDocument)mapping.DeepClone());
        }

        Store(Fields(target.Parent), target.Name, target.Mappings);
    }

    private static BsonDocument Fields(BsonDocument document)
    {
        if (document.GetValue("fields", BsonNull.Value) is not BsonDocument fields)
        {
            document["fields"] = fields = [];
        }

        return fields;
    }

    private static List<BsonDocument> MappingsOf(BsonDocument fields,
        string name) =>
        fields.GetValue(name, BsonNull.Value) switch
        {
            BsonDocument one => [one],
            BsonArray many => [.. many.OfType<BsonDocument>()],
            _ => []
        };

    // A field mapped one way is a document, as it's usually written; mapped several ways, an array.
    private static void Store(BsonDocument fields,
        string name,
        List<BsonDocument> mappings) =>
        fields[name] = mappings.Count == 1 ? mappings[0] : new BsonArray(mappings);

    private static void ThrowIfNoFields(SearchField[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Length == 0 || fields.Any(field => field is null))
        {
            throw new ArgumentException("Map takes at least one SearchField, such as SearchField.String(), and no nulls.",
                nameof(fields));
        }
    }
}
