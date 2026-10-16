using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vector search index a collection declares, rendered once the collection's serializers are known.</summary>
internal sealed class VectorSearchIndexBuilder<TDocument>(string name) : IVectorSearchIndexBuilder<TDocument>
{
    private const int MaxDimensions = 8192;

    private readonly List<(LambdaExpression Member, int Dimensions, VectorSimilarity Similarity, VectorQuantization? Quantization)> _vectors = [];
    private readonly List<(LambdaExpression? Member, string? Path)> _filters = [];
    private BsonDocument? _definition;

    public string Name { get; } = name;

    public IVectorSearchIndexBuilder<TDocument> Definition(BsonDocument definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = (BsonDocument)definition.DeepClone();
        return this;
    }

    public IVectorSearchIndexBuilder<TDocument> Vector<TMember>(Expression<Func<TDocument, TMember>> member,
        int dimensions,
        VectorSimilarity similarity,
        VectorQuantization? quantization = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions, MaxDimensions);

        _vectors.Add((member, dimensions, similarity, quantization));
        return this;
    }

    public IVectorSearchIndexBuilder<TDocument> Filter<TMember>(Expression<Func<TDocument, TMember>> member)
    {
        ArgumentNullException.ThrowIfNull(member);

        _filters.Add((member, null));
        return this;
    }

    public IVectorSearchIndexBuilder<TDocument> Filter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _filters.Add((null, path));
        return this;
    }

    /// <summary>
    /// The definition: the one given, with the vectors and filter fields declared added, and the fields
    /// <paramref name="features"/> filter on as filter fields.
    /// </summary>
    /// <exception cref="VaultConfigurationException">It indexes no vector.</exception>
    public SearchIndexDeclaration Build(RenderArgs<TDocument> args,
        IReadOnlyList<FeatureField> features,
        string collection)
    {
        var definition = _definition is null ? new BsonDocument() : (BsonDocument)_definition.DeepClone();
        if (definition.GetValue("fields", BsonNull.Value) is not BsonArray fields)
        {
            definition["fields"] = fields = [];
        }

        foreach (var (member, dimensions, similarity, quantization) in _vectors)
        {
            var vector = new BsonDocument
            {
                { "type", "vector" },
                { "path", FilterDocuments.RenderField(member, args).FieldName },
                { "numDimensions", dimensions },
                { "similarity", Similarity(similarity) }
            };

            if (quantization is { } compressed && compressed != VectorQuantization.None)
            {
                vector["quantization"] = Quantization(compressed);
            }

            Put(fields, vector);
        }

        var paths = _filters.Select(filter => filter.Member is null ? filter.Path! : FilterDocuments.RenderField(filter.Member, args).FieldName)
            .Concat(features.Where(feature => feature.SearchType is not null).Select(feature => feature.Path));

        foreach (var path in paths)
        {
            Put(fields, new BsonDocument { { "type", "filter" }, { "path", path } });
        }

        if (!fields.OfType<BsonDocument>().Any(field => field.GetValue("type", BsonNull.Value) == "vector"))
        {
            throw new VaultConfigurationException(
                $"The vector search index {Name} of {collection} indexes no vector. Declare one with Vector, such as " +
                ".Vector(x => x.Embedding, dimensions: 1536, VectorSimilarity.Cosine).");
        }

        return new SearchIndexDeclaration(Name, SearchIndexType.VectorSearch, definition);
    }

    // A field of the same type and path, declared again, takes the earlier one's place.
    private static void Put(BsonArray fields,
        BsonDocument field)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i] is BsonDocument existing && existing.GetValue("type", BsonNull.Value) == field["type"] &&
                existing.GetValue("path", BsonNull.Value) == field["path"])
            {
                fields[i] = field;
                return;
            }
        }

        fields.Add(field);
    }

    private static string Similarity(VectorSimilarity similarity) =>
        similarity switch
        {
            VectorSimilarity.Euclidean => "euclidean",
            VectorSimilarity.DotProduct => "dotProduct",
            _ => "cosine"
        };

    private static string Quantization(VectorQuantization quantization) =>
        quantization switch
        {
            VectorQuantization.Scalar => "scalar",
            VectorQuantization.Binary => "binary",
            _ => "binaryNoRescore"
        };
}
