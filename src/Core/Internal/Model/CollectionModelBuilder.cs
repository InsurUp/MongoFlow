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

    public abstract ICollectionModel Build(IMongoDatabase database);

    /// <summary>How the vault's property is filled with this collection. Called after <see cref="Build"/>.</summary>
    public abstract CollectionBinding<TVault> Bind<TVault>() where TVault : MongoVault;
}
