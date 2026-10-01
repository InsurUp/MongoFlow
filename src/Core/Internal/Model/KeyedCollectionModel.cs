namespace MongoFlow;

internal sealed class KeyedCollectionModel<TDocument, TKey> : CollectionModel<TDocument>
{
    public KeyedCollectionModel(CollectionDefinition<TDocument> definition,
        KeyModel<TDocument, TKey> key)
        : base(definition)
    {
        Key = key;
    }

    public KeyModel<TDocument, TKey> Key { get; }

    public KeyTarget<TDocument> Target(TKey key) => new KeyTarget<TDocument, TKey>(Key, key);

    public override IVaultCollection<TDocument> CreateCollection(VaultRuntime runtime,
        IReadOnlySet<FeatureKey> disabled) =>
        CreateKeyedCollection(runtime, disabled);

    public IVaultCollection<TDocument, TKey> CreateKeyedCollection(VaultRuntime runtime,
        IReadOnlySet<FeatureKey> disabled) =>
        new KeyedVaultCollection<TDocument, TKey>(runtime, this, disabled);
}
