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

    public override IReadOnlyList<string> KeyFields => Key.FieldNames;

    public override object? KeyOf(TDocument document) => Key.Get(document);

    public KeyTarget<TDocument> Target(TKey key) => new KeyTarget<TDocument, TKey>(Key, key);

    public override VaultCollection<TDocument> CreateCollection(VaultRuntime runtime,
        FeatureSet disabled) =>
        CreateKeyedCollection(runtime, disabled);

    /// <summary>A view that tracks changes if the vault does.</summary>
    public KeyedVaultCollection<TDocument, TKey> CreateKeyedCollection(VaultRuntime runtime,
        FeatureSet disabled) =>
        new(runtime, this, disabled, runtime.Model.TracksChanges);
}
