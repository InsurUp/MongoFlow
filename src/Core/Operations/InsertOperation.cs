using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Inserts one document. <c>AddRange</c> queues one per document, so every inserted document is seen the same way.
/// </summary>
public sealed class InsertOperation<TDocument> : VaultOperation<TDocument>
{
    internal InsertOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        TDocument document)
        : base(model, disabledFeatures, target: null)
    {
        Document = document;
    }

    /// <inheritdoc/>
    public override OperationKind Kind => OperationKind.Insert;

    /// <inheritdoc/>
    public override bool IsSetBased => false;

    internal override ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<BulkWriteModel>(new BulkWriteInsertOneModel<TDocument>(Namespace, Document!));
}
