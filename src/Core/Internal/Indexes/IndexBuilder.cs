using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>An index's options as declared, rendered with its keys once the collection's serializers are known.</summary>
internal sealed class IndexBuilder<TDocument> : IIndexBuilder<TDocument>
{
    private string? _name;
    private bool _unique;
    private bool _sparse;
    private FilterDefinition<TDocument>? _partial;
    private TimeSpan? _expireAfter;
    private bool _hidden;
    private Collation? _collation;

    public IIndexBuilder<TDocument> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _name = name;
        return this;
    }

    public IIndexBuilder<TDocument> Unique(bool unique = true)
    {
        _unique = unique;
        return this;
    }

    public IIndexBuilder<TDocument> Sparse(bool sparse = true)
    {
        _sparse = sparse;
        return this;
    }

    public IIndexBuilder<TDocument> Partial(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return Partial(Builders<TDocument>.Filter.Where(filter));
    }

    public IIndexBuilder<TDocument> Partial(FilterDefinition<TDocument> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _partial = filter;
        return this;
    }

    public IIndexBuilder<TDocument> ExpireAfter(TimeSpan expireAfter)
    {
        if (expireAfter < TimeSpan.Zero || expireAfter.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expireAfter), expireAfter, "An expiry is whole seconds, zero or more.");
        }

        _expireAfter = expireAfter;
        return this;
    }

    public IIndexBuilder<TDocument> Hidden(bool hidden = true)
    {
        _hidden = hidden;
        return this;
    }

    public IIndexBuilder<TDocument> Collation(Collation collation)
    {
        ArgumentNullException.ThrowIfNull(collation);

        _collation = collation;
        return this;
    }

    /// <summary>The index on <paramref name="keys"/>, rendered with <paramref name="args"/>.</summary>
    public IndexDeclaration Build(IndexKeysDefinition<TDocument> keys,
        RenderArgs<TDocument> args)
    {
        var rendered = keys.Render(args);

        return new IndexDeclaration(_name ?? IndexNaming.Of(rendered),
            rendered,
            _unique,
            _sparse,
            _partial?.Render(args),
            _expireAfter,
            _hidden,
            _collation);
    }

    /// <summary>
    /// The index <paramref name="configure"/> declares on <paramref name="keys"/>, for a collection that isn't a vault's.
    /// </summary>
    /// <exception cref="ArgumentException">The server would refuse the index.</exception>
    public static IndexDeclaration Declare(IMongoCollection<TDocument> collection,
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<IIndexBuilder<TDocument>>? configure)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(keys);

        var builder = new IndexBuilder<TDocument>();
        configure?.Invoke(builder);

        var declaration = builder.Build(Keys(keys), FilterDocuments.RenderArgs(collection));
        return IndexSpecs.Invalid(declaration) is { } reason
            ? throw new ArgumentException($"{IndexSpecs.Described(declaration)} can't be created: {reason}.", nameof(keys))
            : declaration;
    }

    /// <summary>The keys <paramref name="keys"/> builds.</summary>
    /// <exception cref="ArgumentException">It builds none.</exception>
    public static IndexKeysDefinition<TDocument> Keys(
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys) =>
        keys(Builders<TDocument>.IndexKeys) ?? throw new ArgumentException("The keys builder returned no keys.", nameof(keys));
}
