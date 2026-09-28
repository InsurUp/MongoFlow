using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Replaces the document with the same key.</summary>
public sealed class ReplaceOperation<TDocument> : VaultOperation<TDocument>
{
    internal ReplaceOperation(IVaultCollectionInfo collection,
        CollectionNamespace @namespace,
        IReadOnlySet<FeatureKey> disabledFeatures,
        object key,
        TDocument document)
        : base(collection, @namespace, disabledFeatures)
    {
        Key = key;
        Document = document;
    }

    public override OperationKind Kind => OperationKind.Replace;

    public override bool IsSetBased => false;

    /// <summary>The key read from the document when it was queued.</summary>
    public object Key { get; }
}
