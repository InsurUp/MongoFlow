using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

internal abstract class CollectionModelBuilder(VaultModelBuilderBase vault, PropertyInfo property, Type documentType, Type? keyType)
    : IVaultCollectionInfo
{
    protected VaultModelBuilderBase Vault { get; } = vault;

    public PropertyInfo Property { get; } = property;

    public Type DocumentType { get; } = documentType;

    public Type? KeyType { get; } = keyType;

    public string PropertyName => Property.Name;

    /// <summary>The built collection, once <see cref="Build"/> has run.</summary>
    public ICollectionModel? Model { get; protected set; }

    /// <summary>The features this collection opted out of in configuration.</summary>
    public abstract IReadOnlySet<FeatureKey> OptedOut { get; }

    public abstract void Accept(IVaultCollectionConfiguration configuration);

    public abstract void Apply(VaultQueryFilter filter);

    /// <summary>Adds <paramref name="pending"/> if the collection's documents are assignable to <typeparamref name="TTarget"/>.</summary>
    public abstract void RunSaveWhen<TTarget>(Func<TTarget, bool> pending,
        FeatureKey? owner);

    public abstract ICollectionModel Build(IMongoDatabase database);
}
