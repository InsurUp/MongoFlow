using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Replaces the document with the same key.</summary>
public sealed class ReplaceOperation<TDocument> : VaultOperation<TDocument>
{
    internal ReplaceOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        KeyTarget<TDocument> target,
        TDocument document)
        : base(model, disabledFeatures, target)
    {
        Document = document;
    }

    public override OperationKind Kind => OperationKind.Replace;

    public override bool IsSetBased => false;

    /// <summary>The key read from the document when it was queued.</summary>
    public object Key => Target!.Key;

    internal override ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        TypedModel.CreateWriteModelAsync(this, run, cancellationToken);
}
