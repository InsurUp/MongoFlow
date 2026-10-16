using MongoDB.Bson;
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

    /// <summary>
    /// The key of the document, read from it each time, so a key the write fills in, such as an empty
    /// <c>ObjectId</c>, shows once the document is written. <see langword="null"/> for a keyless collection.
    /// </summary>
    public override object? Key => TypedModel.KeyOf(Document!);

    internal override ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<BulkWriteModel>(new BulkWriteInsertOneModel<TDocument>(Namespace, Document!));

    // The inserts into the collection that follow it go in the same command, in order.
    internal override async ValueTask<int> WriteAsync(SaveRun run,
        int index,
        OperationResult[] results,
        CancellationToken cancellationToken)
    {
        var operations = run.Operations;
        var count = 1;
        while (index + count < operations.Count &&
               operations[index + count] is InsertOperation<TDocument> next && ReferenceEquals(next.Collection, Collection))
        {
            count++;
        }

        try
        {
            if (TypedModel.Encryption is { } encryption)
            {
                var documents = new List<BsonDocument>(count);
                for (var i = index; i < index + count; i++)
                {
                    var document = TypedModel.Serialize(((InsertOperation<TDocument>)operations[i]).Document!, assignId: true);
                    documents.Add(await encryption.EncryptDocumentAsync(document, cancellationToken));
                }

                await TypedModel.BsonCollection.InsertManyAsync(run.Session, documents, cancellationToken: cancellationToken);
            }
            else
            {
                var documents = new List<TDocument>(count);
                for (var i = index; i < index + count; i++)
                {
                    documents.Add(((InsertOperation<TDocument>)operations[i]).Document!);
                }

                await TypedModel.MongoCollection.InsertManyAsync(run.Session, documents, cancellationToken: cancellationToken);
            }
        }
        catch (MongoBulkWriteException exception) when (DuplicateKeyAt(exception) is { } refused)
        {
            // The insert is ordered, so it stopped at the one it refused, which it counts from this one.
            throw new DuplicateKeyException(operations[index + refused], exception);
        }

        for (var i = index; i < index + count; i++)
        {
            results[i] = OperationResult.Of(1, 0, 0, 0);
        }

        return count;
    }

    private static int? DuplicateKeyAt(MongoBulkWriteException exception) =>
        exception.WriteErrors.FirstOrDefault(error => error.Category == ServerErrorCategory.DuplicateKey)?.Index;
}
