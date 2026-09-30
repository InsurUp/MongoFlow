using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class CollectionModelBuilder<TDocument, TKey>(VaultModelBuilderBase vault, PropertyInfo property)
    : CollectionModelBuilder<TDocument>(vault, property, typeof(TKey)), IVaultCollectionBuilder<TDocument, TKey>
{
    private readonly Layered<(Expression<Func<TDocument, TKey>> Key, bool Unique)> _key = new();
    private readonly Layered<ConcurrencyTokenModel<TDocument>> _token = new();

    public IVaultCollectionBuilder<TDocument, TKey> Key(Expression<Func<TDocument, TKey>> key, bool unique = true)
    {
        ArgumentNullException.ThrowIfNull(key);

        _key.Set(Vault.Layer, (key, unique));
        return this;
    }

    public IVaultCollectionBuilder<TDocument, TKey> ConcurrencyToken<TToken>(Expression<Func<TDocument, TToken>> token)
        where TToken : INumber<TToken>
    {
        ArgumentNullException.ThrowIfNull(token);

        _token.Set(Vault.Layer, new ConcurrencyTokenModel<TDocument, TToken>(token));
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.Name(string name)
    {
        Name(name);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.QueryFilter(
        Expression<Func<TDocument, bool>> filter)
    {
        QueryFilter(filter);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.QueryFilter(
        Func<IServiceProvider, Expression<Func<TDocument, bool>>> filter)
    {
        QueryFilter(filter);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.QueryFilter(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TDocument, bool>>>> filter)
    {
        QueryFilter(filter);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.Without(FeatureKey feature)
    {
        Without(feature);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.Index(
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<CreateIndexOptions<TDocument>>? options)
    {
        Index(keys, options);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.Settings(Action<MongoCollectionSettings> configure)
    {
        Settings(configure);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.CreateWith(
        Action<CreateCollectionOptions<TDocument>> configure)
    {
        CreateWith(configure);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.AddInterceptor<TInterceptor>(
        Action<IInterceptorBuilder>? configure)
    {
        AddInterceptor<TInterceptor>(configure);
        return this;
    }

    IVaultCollectionBuilder<TDocument, TKey> IVaultCollectionBuilder<TDocument, TKey>.AddInterceptor(VaultInterceptor interceptor,
        Action<IInterceptorBuilder>? configure)
    {
        AddInterceptor(interceptor, configure);
        return this;
    }

    private KeyedCollectionModel<TDocument, TKey> _keyedModel = null!;

    public override CollectionBinding<TVault> Bind<TVault>() =>
        new CollectionBinding<TVault, IVaultCollection<TDocument, TKey>>(Property,
            runtime => _keyedModel.CreateKeyedCollection(runtime, FeatureKeys.None));

    protected override CollectionModel<TDocument> CreateModel(CollectionDefinition<TDocument> definition)
    {
        var (key, unique) = _key.TryGet(out var configured) ? configured : (null, false);
        var keyModel = KeyModel<TDocument, TKey>.Create(key, unique, definition.Collection.CollectionNamespace.CollectionName);

        if (keyModel.UniqueIndex is { } uniqueIndex)
        {
            definition.Indexes.Insert(0, uniqueIndex);
        }

        return _keyedModel = new KeyedCollectionModel<TDocument, TKey>(definition,
            keyModel,
            _token.TryGet(out var token) ? token : null);
    }
}
