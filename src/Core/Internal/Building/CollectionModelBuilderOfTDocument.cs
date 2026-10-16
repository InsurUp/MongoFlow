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
    private readonly List<(Func<TDocument, bool> Pending, FeatureKey? Owner)> _runSaveWhen = [];
    private readonly List<LambdaExpression> _reserved = [];
    private readonly List<(LambdaExpression Member, object Options)> _encrypted = [];
    private readonly Dictionary<string, SearchIndexBuilder<TDocument>> _searchIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VectorSearchIndexBuilder<TDocument>> _vectorSearchIndexes = new(StringComparer.Ordinal);

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

    public IVaultCollectionBuilder<TDocument> QueryFilter(LambdaExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return QueryFilter(FilterExpressions.ForDocument<TDocument>(filter) ??
                           throw new ArgumentException(FilterExpressions.Unusable<TDocument>(filter), nameof(filter)));
    }

    public IVaultCollectionBuilder<TDocument> QueryFilter(Func<IServiceProvider, LambdaExpression> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return QueryFilter(services => Usable(filter(services)));
    }

    public IVaultCollectionBuilder<TDocument> QueryFilter(
        Func<IServiceProvider, CancellationToken, ValueTask<LambdaExpression>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return QueryFilter(async (services, cancellationToken) => Usable(await filter(services, cancellationToken)));
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

    public IVaultCollectionBuilder<TDocument> SearchIndex(string name,
        Action<ISearchIndexBuilder<TDocument>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_searchIndexes.TryGetValue(name, out var index))
        {
            _searchIndexes[name] = index = new SearchIndexBuilder<TDocument>(name);
        }

        configure(index);
        return this;
    }

    public IVaultCollectionBuilder<TDocument> VectorSearchIndex(string name,
        Action<IVectorSearchIndexBuilder<TDocument>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_vectorSearchIndexes.TryGetValue(name, out var index))
        {
            _vectorSearchIndexes[name] = index = new VectorSearchIndexBuilder<TDocument>(name);
        }

        configure(index);
        return this;
    }

    /// <summary>
    /// Notes that every read filters on <paramref name="member"/>, such as <c>(ISoftDeletable x) =&gt; x.IsDeleted</c>, so
    /// an index should include it; the index check warns when none does. For built-in features, which also reserve it.
    /// </summary>
    public void ExpectIndex(LambdaExpression member)
    {
        var field = OnDocument(member);
        _indexed.Add((field, Vault.Owner));
        _reserved.Add(field);
    }

    /// <summary>
    /// Notes that a built-in feature filters or writes on <paramref name="member"/>, such as the concurrency token's, so it
    /// can't be encrypted: features compare it with null, increment it, or match it in every write.
    /// </summary>
    public void ReserveField(LambdaExpression member) => _reserved.Add(OnDocument(member));

    /// <summary>
    /// Declares <paramref name="member"/>, such as <c>x =&gt; x.Address.Street</c>, encrypted, with what MongoFlow.Encryption
    /// declared it with.
    /// </summary>
    public void Encrypt(LambdaExpression member,
        object options) =>
        _encrypted.Add((member, options));

    public override void Accept(IVaultCollectionConfiguration configuration) => configuration.Configure(this);

    public override void Apply(VaultQueryFilter filter)
    {
        if (filter.For<TDocument>() is { } entry)
        {
            _filters.Add(filter.Layer, entry);
        }
    }

    public override void RunSaveWhen<TTarget>(Func<TTarget, bool> pending,
        FeatureKey? owner)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TTarget)))
        {
            _runSaveWhen.Add((document => pending((TTarget)(object)document!), owner));
        }
    }

    public override IReadOnlySet<FeatureKey> OptedOut => _without;

    public override ICollectionModel Build(IMongoDatabase database,
        VaultEncryption? encryption,
        VaultLogs logs)
    {
        var definition = new CollectionDefinition<TDocument>(
            Property,
            KeyType,
            database.GetCollection<TDocument>(_name.TryGet(out var name) ? name : Property.Name),
            [.. _filters.All.Where(filter => IsOn(filter.Owner))],
            [.. _indexed.Where(indexed => IsOn(indexed.Owner)).Select(indexed => (indexed.Field, indexed.Owner?.Name ?? "a query filter"))],
            [.. _runSaveWhen.Where(pending => IsOn(pending.Owner))]);

        EnsureIdIsRead(definition.Collection);

        var model = CreateModel(definition);
        if (_encrypted.Count > 0)
        {
            model.EncryptWith(EncryptionOf(model, encryption));
        }

        model.SearchWith(SearchOf(model, logs));

        return Model = model;
    }

    protected virtual CollectionModel<TDocument> CreateModel(CollectionDefinition<TDocument> definition) =>
        new(definition);

    private bool IsOn(FeatureKey? owner) => owner is not { } feature || !_without.Contains(feature);

    /// <summary>
    /// <paramref name="member"/>, such as <c>(ISoftDeletable x) =&gt; x.IsDeleted</c>, as the member alone, looking through a
    /// conversion of its value, retyped onto the document.
    /// </summary>
    private static LambdaExpression OnDocument(LambdaExpression member)
    {
        var source = member.Parameters[0];
        var parameter = Expression.Parameter(typeof(TDocument), source.Name);
        var argument = source.Type == typeof(TDocument) ? parameter : (Expression)Expression.Convert(parameter, source.Type);

        var field = ParameterReplacer.Inline(Expression.Lambda(member.GetMember(nameof(member)), source), argument);
        return Expression.Lambda(field, parameter);
    }

    /// <summary>How <paramref name="model"/> encrypts its encrypted members, once they're checked.</summary>
    /// <exception cref="VaultConfigurationException">A member can't be encrypted, or the documents can't be read back.</exception>
    private ICollectionEncryption EncryptionOf(CollectionModel<TDocument> model,
        VaultEncryption? encryption)
    {
        var name = model.Namespace.CollectionName;
        if (encryption is null)
        {
            throw new VaultConfigurationException(
                $"{PropertyName} declares encrypted members, but its vault doesn't encrypt. Call UseQueryableEncryption " +
                "on the vault.");
        }

        EnsureExtraElementsIgnored(model.MongoCollection);

        var reserved = _reserved.Select(field => FilterDocuments.RenderField(field, model.RenderArgs).FieldName).ToList();
        var members = new List<EncryptedMember>(_encrypted.Count);

        foreach (var (member, options) in _encrypted)
        {
            var field = FilterDocuments.RenderField(member, model.RenderArgs);
            var path = field.FieldName;
            var described = $"{member}, stored as {path} in {name},";

            string? reason = null;
            if (ElementPaths.Reaches(path, "_id"))
            {
                reason = "it's the _id, or reaches it";
            }
            else if (model.KeyFields?.FirstOrDefault(key => ElementPaths.Reaches(path, key)) is { } key)
            {
                reason = key == path
                    ? "it's the key, which writes by key match"
                    : $"it reaches the key, {key}, which writes by key match";
            }
            else if (reserved.FirstOrDefault(field => ElementPaths.Reaches(path, field)) is { } feature)
            {
                const string Features = "a feature, such as soft delete, multi-tenancy or the concurrency token,";
                reason = feature == path
                    ? $"{Features} filters on it or changes it"
                    : $"it reaches {feature}, which {Features} filters on or changes";
            }
            else if (members.FirstOrDefault(other => ElementPaths.Reaches(path, other.Path)) is { } other)
            {
                reason = other.Path == path
                    ? "it's declared encrypted twice"
                    : $"it holds, or is inside, {other.Path}, which is encrypted too: an encrypted field can't hold another";
            }

            if (reason is not null)
            {
                throw new VaultConfigurationException($"{described} can't be encrypted: {reason}.");
            }

            members.Add(new EncryptedMember(path, field.FieldSerializer, options));
        }

        return encryption.ForCollection(model, members);
    }

    /// <summary>The collection's searches: its declared indexes rendered, with the fields its features filter on mapped.</summary>
    /// <exception cref="VaultConfigurationException">An index can't be declared as it is.</exception>
    private CollectionSearch SearchOf(CollectionModel<TDocument> model,
        VaultLogs logs)
    {
        var name = model.Namespace.CollectionName;
        var declarations = new List<SearchIndexDeclaration>(_searchIndexes.Count + _vectorSearchIndexes.Count);

        if (declarations.Capacity > 0)
        {
            if (_searchIndexes.Keys.Intersect(_vectorSearchIndexes.Keys).FirstOrDefault() is { } both)
            {
                throw new VaultConfigurationException(
                    $"{PropertyName} declares {both} as a search index and as a vector search index. An index has one " +
                    "name per collection.");
            }

            if (model.Encryption is not null)
            {
                throw new VaultConfigurationException(
                    $"{PropertyName} declares search indexes, but has encrypted members: MongoDB Search can't index a " +
                    "collection with Queryable Encryption. Search another collection, which holds what can be searched " +
                    "in plain text.");
            }

            var features = model.RenderFeatureFields();
            declarations.AddRange(_searchIndexes.Values.Select(index => index.Build(model.RenderArgs, features)));
            declarations.AddRange(_vectorSearchIndexes.Values.Select(index => index.Build(model.RenderArgs, features, name)));
        }

        var collection = model.MongoCollection;
        return new CollectionSearch(name,
            declarations,
            async (index, cancellationToken) =>
                await (await collection.SearchIndexes.ListAsync(index, cancellationToken: cancellationToken))
                    .FirstOrDefaultAsync(cancellationToken),
            logs.Search);
    }

    /// <summary>
    /// Fails unless the documents, and the ones derived from them, ignore extra elements and have no member for them: the
    /// server adds <c>__safeContent__</c> to each encrypted document, which reading back would fail on, and a member
    /// holding it would send it back with a replace, which the server refuses.
    /// </summary>
    private static void EnsureExtraElementsIgnored(IMongoCollection<TDocument> collection)
    {
        var name = collection.CollectionNamespace.CollectionName;
        if (collection.DocumentSerializer is not BsonClassMapSerializer<TDocument>)
        {
            throw new VaultConfigurationException(
                $"{typeof(TDocument).Name}, in {name}, has encrypted members, so it has to be serialized with a class map, " +
                $"which MongoFlow finds them by. Its serializer is {collection.DocumentSerializer.GetType().Name}.");
        }

        var classMaps = BsonClassMap.GetRegisteredClassMaps()
            .Where(classMap => classMap.ClassType.IsAssignableTo(typeof(TDocument)))
            .Prepend(BsonClassMap.LookupClassMap(typeof(TDocument)))
            .Distinct();

        foreach (var classMap in classMaps)
        {
            if (classMap.ExtraElementsMemberMap is { } extra)
            {
                throw new VaultConfigurationException(
                    $"{classMap.ClassType.Name}, in {name}, has encrypted members, so it can't keep extra elements in " +
                    $"{extra.MemberName}: it would hold the __safeContent__ the server adds to each encrypted document, " +
                    "and send it back on replace, which the server refuses. Remove the member and ignore extra elements.");
            }

            if (!classMap.IgnoreExtraElements)
            {
                throw new VaultConfigurationException(
                    $"{classMap.ClassType.Name}, in {name}, has encrypted members, so it must ignore extra elements: the " +
                    "server adds __safeContent__ to each encrypted document, and reading it back would fail. Mark it " +
                    "[BsonIgnoreExtraElements].");
            }
        }
    }

    /// <summary>A filter resolved per query, as a filter of the collection's documents.</summary>
    /// <exception cref="InvalidOperationException">It can't filter them.</exception>
    private static Expression<Func<TDocument, bool>> Usable(LambdaExpression filter) =>
        FilterExpressions.ForDocument<TDocument>(filter) ??
        throw new InvalidOperationException(FilterExpressions.Unusable<TDocument>(filter));

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
