using System.Linq.Expressions;

namespace MongoFlow;

/// <summary>How a keyed collection reads a document's key and finds a document by key.</summary>
internal sealed class KeyModel<TDocument, TKey>
{
    private readonly Func<TDocument, TKey> _get;
    private readonly Func<TKey, Expression<Func<TDocument, bool>>> _filter;

    private KeyModel(Expression<Func<TDocument, TKey>> key,
        Func<TKey, Expression<Func<TDocument, bool>>> filter)
    {
        _get = key.Compile();
        _filter = filter;
    }

    public TKey Get(TDocument document) => _get(document);

    public Expression<Func<TDocument, bool>> Filter(TKey key) => _filter(key);

    /// <param name="key">The configured key, or <see langword="null"/> for the member the driver maps to <c>_id</c>.</param>
    /// <param name="collection">The collection's name, for error messages.</param>
    public static KeyModel<TDocument, TKey> Create(Expression<Func<TDocument, TKey>>? key,
        string collection)
    {
        key ??= KeyExpressions.Id<TDocument, TKey>(collection);

        return new KeyModel<TDocument, TKey>(key, key.Body is NewExpression composite
            ? KeyExpressions.CompositeFilter(key, composite, collection)
            : KeyExpressions.MemberFilter(key));
    }
}
