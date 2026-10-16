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

    internal override async ValueTask<int> WriteAsync(SaveRun run,
        int index,
        OperationResult[] results,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterDocumentAsync(run, null, cancellationToken);
        ReplaceOneResult written;

        try
        {
            if (TypedModel.Encryption is { } encryption)
            {
                var replacement = await encryption.EncryptDocumentAsync(TypedModel.Serialize(Document!, assignId: false),
                    cancellationToken);
                written = await TypedModel.BsonCollection.ReplaceOneAsync(run.Session, filter, replacement,
                    cancellationToken: cancellationToken);
            }
            else
            {
                written = await TypedModel.MongoCollection.ReplaceOneAsync(run.Session,
                    new BsonDocumentFilterDefinition<TDocument>(filter), Document!, cancellationToken: cancellationToken);
            }
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            throw new DuplicateKeyException(this, exception);
        }

        results[index] = OperationResult.Of(0, written.MatchedCount, written.ModifiedCount, 0);
        return 1;
    }
}
