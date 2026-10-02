using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>How a keyed collection reads a document's key and matches a document by key.</summary>
internal sealed class KeyModel<TDocument, TKey>
{
    private readonly Func<TDocument, TKey> _get;
    private readonly (RenderedFieldDefinition Field, Func<TKey, object?> Read)[] _parts;

    private KeyModel(Expression<Func<TDocument, TKey>> key,
        (RenderedFieldDefinition Field, Func<TKey, object?> Read)[] parts)
    {
        _get = key.Compile();
        _parts = parts;
        FieldNames = [.. parts.Select(part => part.Field.FieldName)];
    }

    /// <summary>The element names the key matches, such as <c>["UserId", "provider"]</c>.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    public TKey Get(TDocument document) => _get(document);

    /// <summary>The filter that matches the document with <paramref name="key"/>, such as <c>{ PolicyNumber: "P-1" }</c>.</summary>
    public BsonDocument Match(TKey key)
    {
        var filter = new BsonDocument();

        foreach (var (field, read) in _parts)
        {
            filter.Add(FilterDocuments.Equal(field, read(key)));
        }

        return filter;
    }

    /// <param name="key">The configured key, or <see langword="null"/> for the member the driver maps to <c>_id</c>.</param>
    /// <param name="collection">The collection, whose serializers render the key's members.</param>
    public static KeyModel<TDocument, TKey> Create(Expression<Func<TDocument, TKey>>? key,
        IMongoCollection<TDocument> collection)
    {
        var name = collection.CollectionNamespace.CollectionName;
        key ??= KeyExpressions.Id<TDocument, TKey>(name);

        var parts = key.Body is NewExpression composite
            ? KeyExpressions.CompositeParts(key, composite, name)
            : [KeyExpressions.MemberPart(key, name)];

        var args = FilterDocuments.RenderArgs(collection);

        return new KeyModel<TDocument, TKey>(key, 
            [.. parts.Select(part => (FilterDocuments.RenderField(part.Member, args), part.Read))]);
    }
}
