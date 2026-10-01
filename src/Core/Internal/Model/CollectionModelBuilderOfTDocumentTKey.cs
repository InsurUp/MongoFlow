using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

internal sealed class CollectionModelBuilder<TDocument, TKey>(VaultModelBuilderBase vault, PropertyInfo property)
    : CollectionModelBuilder<TDocument>(vault, property, typeof(TKey)), IVaultCollectionBuilder<TDocument, TKey>
{
    private readonly Layered<Expression<Func<TDocument, TKey>>> _key = new();

    public IVaultCollectionBuilder<TDocument, TKey> Key(Expression<Func<TDocument, TKey>> key)
    {
        ArgumentNullException.ThrowIfNull(key);

        _key.Set(Vault.Layer, key);
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
        var keyModel = KeyModel<TDocument, TKey>.Create(_key.TryGet(out var key) ? key : null,
            definition.Collection.CollectionNamespace.CollectionName);

        return _keyedModel = new KeyedCollectionModel<TDocument, TKey>(definition, keyModel);
    }
}
