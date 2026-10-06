using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault collection as built at startup: shared by every vault instance, never changed.</summary>
internal class CollectionModel<TDocument>(CollectionDefinition<TDocument> definition) : ICollectionModel
{
    private readonly ImmutableArray<QueryFilterEntry<TDocument>> _filters = definition.Filters;
    private readonly bool _hasAsyncFilters = definition.Filters.Any(filter => filter.IsAsync);
    private readonly bool _onlyStatic = definition.Filters.All(filter => filter.Static is not null);
    private readonly ImmutableArray<(LambdaExpression Field, string Feature)> _indexedFields = definition.IndexedFields;

    // The static filters joined once, at startup; the whole filter when every filter is static.
    private readonly Expression<Func<TDocument, bool>>? _staticFilter =
        FilterExpressions.Combine([.. definition.Filters.Select(filter => filter.Static)]);

    public Type DocumentType => typeof(TDocument);

    public Type? KeyType { get; } = definition.KeyType;

    public PropertyInfo Property { get; } = definition.Property;

    public string PropertyName => Property.Name;

    public IMongoCollection<TDocument> MongoCollection { get; } = definition.Collection;

    // Every vault instance fills the property, so its setter is compiled once rather than called through reflection.
    private readonly Action<MongoVault, object> _fill = CompileSetter(definition.Property);

    public CollectionNamespace Namespace => MongoCollection.CollectionNamespace;

    public ConstantExpression MongoCollectionConstant { get; } =
        Expression.Constant(definition.Collection, typeof(IMongoCollection<TDocument>));

    // Outside a session, every read starts from one driver query, as the queries the driver builds on one share it, so
    // it's registered for joins once. Two reads racing to create it both get a registered one.
    private IQueryable<TDocument> SharedQueryable => field ??= VaultJoins.Registered(MongoCollection.AsQueryable(), this);

    public virtual IReadOnlyList<string>? KeyFields => null;

    /// <summary>The key of <paramref name="document"/>, or <see langword="null"/> for a keyless collection.</summary>
    public virtual object? KeyOf(TDocument document) => null;

    /// <summary>Renders the collection's filters and fields the way the driver renders its writes.</summary>
    public RenderArgs<TDocument> RenderArgs { get; } = FilterDocuments.RenderArgs(definition.Collection);

    public virtual VaultCollection<TDocument> CreateCollection(VaultRuntime runtime,
        FeatureSet disabled) =>
        new(runtime, this, disabled);

    public IVaultCollection CreateCollection(VaultRuntime runtime) => CreateCollection(runtime, FeatureSet.Empty);

    // A keyed model's CreateCollection returns a keyed collection, which a keyed property accepts.
    public void Attach(MongoVault vault, VaultRuntime runtime) => _fill(vault, CreateCollection(runtime, FeatureSet.Empty));

    /// <summary>The driver's query of the collection, in <paramref name="session"/> if there is one, registered for joins.</summary>
    public IQueryable<TDocument> Queryable(IClientSessionHandle? session) =>
        session is null ? SharedQueryable : VaultJoins.Registered(MongoCollection.AsQueryable(session), this);

    /// <summary>
    /// The collection's query filters joined into one, minus those of <paramref name="disabled"/> features, or
    /// <see langword="null"/> when nothing filters.
    /// </summary>
    public ValueTask<Expression<Func<TDocument, bool>>?> ResolveFilterAsync(IServiceProvider services,
        FeatureSet disabled,
        CancellationToken cancellationToken)
    {
        if (_filters.Length == 0)
        {
            return ValueTask.FromResult<Expression<Func<TDocument, bool>>?>(null);
        }

        // Every request gets the same filter, so it isn't joined again.
        if (_onlyStatic && disabled.IsEmpty)
        {
            return ValueTask.FromResult(_staticFilter);
        }

        if (_hasAsyncFilters)
        {
            return ResolveAsync(services, disabled, cancellationToken);
        }

        // A disabled filter stays null, which Combine skips.
        var resolved = new Expression<Func<TDocument, bool>>?[_filters.Length];

        for (var i = 0; i < _filters.Length; i++)
        {
            if (IsActive(_filters[i], disabled))
            {
                resolved[i] = _filters[i].Resolve(services);
            }
        }

        return ValueTask.FromResult(FilterExpressions.Combine(resolved));
    }

    public BsonDocument Render(Expression<Func<TDocument, bool>> filter) => ((FilterDefinition<TDocument>)filter).Render(RenderArgs);

    public IEnumerable<IndexedField> RenderIndexedFields() =>
        _indexedFields.Select(indexed =>
            new IndexedField(FilterDocuments.RenderField(indexed.Field, RenderArgs).FieldName, indexed.Feature));

    private async ValueTask<Expression<Func<TDocument, bool>>?> ResolveAsync(IServiceProvider services,
        FeatureSet disabled,
        CancellationToken cancellationToken)
    {
        var resolved = new Expression<Func<TDocument, bool>>?[_filters.Length];

        for (var i = 0; i < _filters.Length; i++)
        {
            if (IsActive(_filters[i], disabled))
            {
                resolved[i] = await _filters[i].ResolveAsync(services, cancellationToken);
            }
        }

        return FilterExpressions.Combine(resolved);
    }

    private static Action<MongoVault, object> CompileSetter(PropertyInfo property)
    {
        var vault = Expression.Parameter(typeof(MongoVault), "vault");
        var value = Expression.Parameter(typeof(object), "value");
        var set = Expression.Call(Expression.Convert(vault, property.DeclaringType!), property.SetMethod!,
            Expression.Convert(value, property.PropertyType));

        return Expression.Lambda<Action<MongoVault, object>>(set, vault, value).Compile();
    }

    private static bool IsActive(QueryFilterEntry<TDocument> filter,
        FeatureSet disabled) =>
        filter.Owner is not { } owner || !disabled.Contains(owner);
}
