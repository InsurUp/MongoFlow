using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace MongoFlow;

internal class CollectionModelBuilder<TDocument> : CollectionModelBuilder, IVaultCollectionBuilder<TDocument>
{
    private readonly Layered<string> _name = new();
    private readonly LayeredList<QueryFilterEntry<TDocument>> _filters = new();
    private readonly HashSet<FeatureKey> _without = [];
    private readonly List<(LambdaExpression Field, FeatureKey? Owner)> _indexed = [];

    public CollectionModelBuilder(VaultModelBuilderBase vault, PropertyInfo property)
        : this(vault, property, keyType: null)
    {
    }

    protected CollectionModelBuilder(VaultModelBuilderBase vault, PropertyInfo property, Type? keyType)
        : base(vault, property, typeof(TDocument), keyType)
    {
    }

    public IVaultCollectionBuilder<TDocument> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _name.Set(Vault.Layer, name);
        return this;
    }

    public IVaultCollectionBuilder<TDocument> QueryFilter(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Vault.Layer, new StaticQueryFilter<TDocument, TDocument>(filter, Vault.Owner));
        return this;
    }

    public IVaultCollectionBuilder<TDocument> QueryFilter(Func<IServiceProvider, Expression<Func<TDocument, bool>>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Vault.Layer, new PerQueryFilter<TDocument, TDocument>(filter, Vault.Owner));
        return this;
    }

    public IVaultCollectionBuilder<TDocument> QueryFilter(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TDocument, bool>>>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Vault.Layer, new AsyncQueryFilter<TDocument, TDocument>(filter, Vault.Owner));
        return this;
    }

    public IVaultCollectionBuilder<TDocument> Without(FeatureKey feature)
    {
        FeatureKeys.ThrowIfDefault(feature, nameof(feature));

        _without.Add(feature);
        return this;
    }

    public IVaultCollectionBuilder<TDocument> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor
    {
        Vault.Register(typeof(TInterceptor), null, this, configure);
        return this;
    }

    public IVaultCollectionBuilder<TDocument> AddInterceptor(VaultInterceptor interceptor, Action<IInterceptorBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(interceptor);

        Vault.Register(null, interceptor, this, configure);
        return this;
    }

    /// <summary>
    /// Notes that every read filters on <paramref name="member"/>, such as <c>(ISoftDeletable x) =&gt; x.IsDeleted</c>, so
    /// an index should include it; the index check warns when none does. For built-in features.
    /// </summary>
    public void ExpectIndex(LambdaExpression member)
    {
        var source = member.Parameters[0];
        var parameter = Expression.Parameter(typeof(TDocument), source.Name);
        var argument = source.Type == typeof(TDocument) ? parameter : (Expression)Expression.Convert(parameter, source.Type);

        // The member alone, looking through a conversion of its value, retyped onto the document.
        var field = ParameterReplacer.Inline(Expression.Lambda(member.GetMember(nameof(member)), source), argument);
        _indexed.Add((Expression.Lambda(field, parameter), Vault.Owner));
    }

    public override void Accept(IVaultCollectionConfiguration configuration) => configuration.Configure(this);

    public override void Apply(VaultQueryFilter filter)
    {
        if (filter.For<TDocument>() is { } entry)
        {
            _filters.Add(filter.Layer, entry);
        }
    }

    public override IReadOnlySet<FeatureKey> OptedOut => _without;

    public override ICollectionModel Build(IMongoDatabase database)
    {
        var definition = new CollectionDefinition<TDocument>(
            Property,
            KeyType,
            database.GetCollection<TDocument>(_name.TryGet(out var name) ? name : Property.Name),
            [.. _filters.All.Where(filter => IsOn(filter.Owner))],
            [.. _indexed.Where(indexed => IsOn(indexed.Owner)).Select(indexed => (indexed.Field, indexed.Owner?.Name ?? "a query filter"))]);

        EnsureIdIsRead(definition.Collection);

        return Model = CreateModel(definition);
    }

    protected virtual CollectionModel<TDocument> CreateModel(CollectionDefinition<TDocument> definition) =>
        new(definition);

    private bool IsOn(FeatureKey? owner) => owner is not { } feature || !_without.Contains(feature);

    /// <summary>
    /// Fails when the documents have no member for the <c>_id</c>, and don't ignore extra elements: the server adds one
    /// to each document, keyless or keyed by other members, and reading it back would fail.
    /// </summary>
    private static void EnsureIdIsRead(IMongoCollection<TDocument> collection)
    {
        // Only a class map can fail to read the _id; a BsonDocument, or a type with a serializer of its own, reads it as it
        // reads anything else.
        if (collection.DocumentSerializer is not BsonClassMapSerializer<TDocument>)
        {
            return;
        }

        var classMap = BsonClassMap.LookupClassMap(typeof(TDocument));
        if (classMap.IdMemberMap is null && !classMap.IgnoreExtraElements)
        {
            throw new VaultConfigurationException(
                $"{typeof(TDocument).Name}, in {collection.CollectionNamespace.CollectionName}, has no member for the " +
                "_id and doesn't ignore extra elements, so reading back the _id the server adds would fail. Mark it " +
                "[BsonIgnoreExtraElements], or give it an Id.");
        }
    }
}
