using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Inserts one document. <c>AddRange</c> queues one per document, so every inserted document is seen the same way.
/// </summary>
public sealed class InsertOperation<TDocument> : VaultOperation<TDocument>
{
    internal InsertOperation(IVaultCollectionInfo collection,
        CollectionNamespace @namespace,
        IReadOnlySet<FeatureKey> disabledFeatures,
        TDocument document)
        : base(collection, @namespace, disabledFeatures)
    {
        Document = document;
    }

    public override OperationKind Kind => OperationKind.Insert;

    public override bool IsSetBased => false;
}
