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

    /// <inheritdoc/>
    public override OperationKind Kind => OperationKind.Replace;

    /// <inheritdoc/>
    public override bool IsSetBased => false;

    /// <summary>The key read from the document when it was queued. A replace always targets one.</summary>
    public new object Key => Target!.Key;

    internal override async ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        new BulkWriteReplaceOneModel<TDocument>(Namespace, await WriteFilterAsync(run, null, cancellationToken), Document!);
}
