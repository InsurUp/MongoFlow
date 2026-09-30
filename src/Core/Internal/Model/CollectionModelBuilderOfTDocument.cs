using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

internal class CollectionModelBuilder<TDocument> : CollectionModelBuilder, IVaultCollectionBuilder<TDocument>
{
    private readonly Layered<string> _name = new();
    private readonly LayeredList<QueryFilterEntry<TDocument>> _filters = new();
    private readonly HashSet<FeatureKey> _without = [];
    private readonly LayeredList<(Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> Keys,
        Action<CreateIndexOptions<TDocument>>? Options)> _indexes = new();
    private readonly LayeredList<Action<MongoCollectionSettings>> _settings = new();
    private readonly LayeredList<Action<CreateCollectionOptions<TDocument>>> _createOptions = new();

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

    public IVaultCollectionBuilder<TDocument> Index(
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<CreateIndexOptions<TDocument>>? options = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _indexes.Add(Vault.Layer, (keys, options));
        return this;
    }

    public IVaultCollectionBuilder<TDocument> Settings(Action<MongoCollectionSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _settings.Add(Vault.Layer, configure);
        return this;
    }

    public IVaultCollectionBuilder<TDocument> CreateWith(Action<CreateCollectionOptions<TDocument>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _createOptions.Add(Vault.Layer, configure);
        return this;
    }

    /// <summary>The built collection, once <see cref="Build"/> has run.</summary>
    protected CollectionModel<TDocument> TypedModel { get; private set; } = null!;

    public override void Accept(IVaultCollectionConfiguration configuration) => configuration.Configure(this);

    public override CollectionBinding<TVault> Bind<TVault>() =>
        new CollectionBinding<TVault, IVaultCollection<TDocument>>(Property,
            runtime => TypedModel.CreateCollection(runtime, FeatureKeys.None));

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
        var name = _name.TryGet(out var configured) ? configured : Property.Name;

        var settings = _settings.All.ToList();
        IMongoCollection<TDocument> collection;
        if (settings.Count == 0)
        {
            collection = database.GetCollection<TDocument>(name);
        }
        else
        {
            var collectionSettings = new MongoCollectionSettings();
            settings.ForEach(configure => configure(collectionSettings));
            collection = database.GetCollection<TDocument>(name, collectionSettings);
        }

        var createOptions = _createOptions.All.ToList();
        CreateCollectionOptions<TDocument>? create = null;
        if (createOptions.Count > 0)
        {
            create = new CreateCollectionOptions<TDocument>();
            createOptions.ForEach(configure => configure(create));
        }

        var definition = new CollectionDefinition<TDocument>(
            Property.Name,
            KeyType,
            collection,
            _filters.All.Where(filter => filter.Owner is not { } owner || !_without.Contains(owner)).ToArray(),
            _indexes.All
                .Select(entry =>
                {
                    var options = new CreateIndexOptions<TDocument>();
                    entry.Options?.Invoke(options);
                    return new CreateIndexModel<TDocument>(entry.Keys(Builders<TDocument>.IndexKeys), options);
                })
                .ToList(),
            create);

        return Model = TypedModel = CreateModel(definition);
    }

    protected virtual CollectionModel<TDocument> CreateModel(CollectionDefinition<TDocument> definition)
    {
        if (typeof(TDocument) is { IsClass: true, IsInterface: false })
        {
            var classMap = BsonClassMap.LookupClassMap(typeof(TDocument));
            if (classMap.IdMemberMap is null && !classMap.IgnoreExtraElements)
            {
                throw new VaultConfigurationException(
                    $"{definition.Collection.CollectionNamespace.CollectionName} is keyless, but {typeof(TDocument).Name} " +
                    "doesn't ignore extra elements, so reading back the _id the server adds would fail. Mark it " +
                    "[BsonIgnoreExtraElements], or give it an Id.");
            }
        }

        return new CollectionModel<TDocument>(definition);
    }
}
