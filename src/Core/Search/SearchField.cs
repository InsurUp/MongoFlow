using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// How a search index maps a field, given to <c>Map</c>: as analyzed text, for autocomplete, as an exact token, or as a
/// number, date, boolean, ObjectId or UUID. A field can be mapped several ways at once, and text indexed again with other
/// analyzers, as alternates (<see cref="Multi"/>).
/// </summary>
public sealed class SearchField
{
    private SearchField(BsonDocument definition)
    {
        Definition = definition;
    }

    /// <summary>The field's mapping, as the search index definition holds it.</summary>
    internal BsonDocument Definition { get; }

    /// <summary>The mapping's type, such as <c>string</c> or <c>token</c>.</summary>
    internal string Type => Definition["type"].AsString;

    /// <summary>Text, analyzed for full-text search with <c>text</c> and <c>phrase</c>.</summary>
    /// <param name="analyzer">The analyzer the text is indexed with, such as <c>lucene.turkish</c>; the index's without.</param>
    /// <param name="searchAnalyzer">The analyzer queries are analyzed with; <paramref name="analyzer"/> without.</param>
    public static SearchField String(string? analyzer = null,
        string? searchAnalyzer = null) =>
        new(new BsonDocument("type", "string")
            .AddIfNotNull("analyzer", analyzer)
            .AddIfNotNull("searchAnalyzer", searchAnalyzer));

    /// <summary>Text, cut into prefixes for <c>autocomplete</c>.</summary>
    /// <param name="minGrams">The shortest prefix indexed.</param>
    /// <param name="maxGrams">The longest prefix indexed.</param>
    /// <param name="tokenization">Where the prefixes are taken from.</param>
    /// <param name="foldDiacritics">Whether <c>ş</c> matches <c>s</c>, and the like.</param>
    /// <param name="analyzer">The analyzer the text is indexed with; <c>lucene.standard</c> without.</param>
    public static SearchField Autocomplete(int minGrams = 2,
        int maxGrams = 15,
        AutocompleteTokenization tokenization = AutocompleteTokenization.EdgeGram,
        bool foldDiacritics = true,
        string? analyzer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minGrams, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGrams, minGrams);

        return new SearchField(new BsonDocument
            {
                { "type", "autocomplete" },
                { "minGrams", minGrams },
                { "maxGrams", maxGrams },
                { "foldDiacritics", foldDiacritics },
                {
                    "tokenization", tokenization switch
                    {
                        AutocompleteTokenization.RightEdgeGram => "rightEdgeGram",
                        AutocompleteTokenization.NGram => "nGram",
                        _ => "edgeGram"
                    }
                }
            }
            .AddIfNotNull("analyzer", analyzer));
    }

    /// <summary>
    /// Text as one exact token, for <c>equals</c>, <c>in</c>, <c>range</c> and sorting: what a search's filters on a
    /// string, such as a tenant id, need.
    /// </summary>
    /// <param name="lowercase">Whether it's lowercased, so equality ignores case.</param>
    public static SearchField Token(bool lowercase = false) =>
        new(lowercase ? new BsonDocument { { "type", "token" }, { "normalizer", "lowercase" } } : new BsonDocument("type", "token"));

    /// <summary>A number, for <c>equals</c>, <c>range</c> and <c>near</c>.</summary>
    public static SearchField Number() => new(new BsonDocument("type", "number"));

    /// <summary>A date, for <c>equals</c>, <c>range</c> and <c>near</c>.</summary>
    public static SearchField Date() => new(new BsonDocument("type", "date"));

    /// <summary>A boolean, for <c>equals</c>.</summary>
    public static SearchField Boolean() => new(new BsonDocument("type", "boolean"));

    /// <summary>An ObjectId, for <c>equals</c>, <c>in</c> and <c>range</c>.</summary>
    public static SearchField ObjectId() => new(new BsonDocument("type", "objectId"));

    /// <summary>A UUID, stored as binary subtype 4, for <c>equals</c> and <c>in</c>.</summary>
    public static SearchField Uuid() => new(new BsonDocument("type", "uuid"));

    /// <summary>
    /// This text mapping with an alternate: the text indexed again as <paramref name="alternate"/> analyzes it, which a
    /// query picks by name with <c>path: { value, multi }</c>, such as a <c>lucene.keyword</c> one for <c>wildcard</c> to
    /// match the whole value. Each call adds one; this mapping is left as it was.
    /// </summary>
    /// <param name="name">The alternate's name, which queries pick it by.</param>
    /// <param name="alternate">How it's indexed: text, such as <c>SearchField.String("lucene.keyword")</c>.</param>
    /// <exception cref="ArgumentException">
    /// This mapping isn't text, <paramref name="alternate"/> isn't text or has alternates of its own, or this mapping has an
    /// alternate named <paramref name="name"/> already.
    /// </exception>
    public SearchField Multi(string name,
        SearchField alternate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(alternate);

        if (Type != "string")
        {
            throw new ArgumentException($"MongoDB Search takes alternates only on text, mapped with SearchField.String(), not on a {Type} mapping.");
        }

        if (alternate.Type != "string" || alternate.Definition.Contains("multi"))
        {
            throw new ArgumentException("An alternate is text, mapped with SearchField.String(), without alternates of its own.",
                nameof(alternate));
        }

        var definition = (BsonDocument)Definition.DeepClone();
        if (definition.GetValue("multi", BsonNull.Value) is not BsonDocument multi)
        {
            definition["multi"] = multi = [];
        }

        if (multi.Contains(name))
        {
            throw new ArgumentException($"The text mapping has an alternate named {name} already.", nameof(name));
        }

        multi[name] = alternate.Definition.DeepClone();
        return new SearchField(definition);
    }

    /// <summary>A mapping written as MongoDB Search takes it, such as <c>{ type: "geo", indexShapes: true }</c>.</summary>
    /// <exception cref="ArgumentException">It has no <c>type</c>.</exception>
    public static SearchField Raw(BsonDocument definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.TryGetValue("type", out var type) && type.IsString
            ? new SearchField((BsonDocument)definition.DeepClone())
            : throw new ArgumentException("A search field's mapping names its type, such as { type: \"token\" }.", nameof(definition));
    }
}
