using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Deletes the document with a key, or every document matching a filter.</summary>
public sealed class DeleteOperation<TDocument> : VaultOperation<TDocument>
{
    internal DeleteOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        KeyTarget<TDocument>? target,
        FilterDefinition<TDocument>? filter,
        TDocument? document)
        : base(model, disabledFeatures, target)
    {
        Filter = filter;
        Document = document;
    }

    /// <inheritdoc/>
    public override OperationKind Kind => OperationKind.Delete;

    /// <inheritdoc/>
    public override bool IsSetBased => Filter is not null;

    /// <summary>
    /// The documents to delete, or <see langword="null"/> when the operation targets a key: the filter it was queued with,
    /// the expression a typed collection was given or the BSON an untyped one was. <see cref="RenderFilter"/> renders
    /// either.
    /// </summary>
    public FilterDefinition<TDocument>? Filter { get; }

    /// <summary>
    /// The same target, document, condition and original as an update, keeping the features switched off. This is how
    /// soft delete turns a delete into setting a flag.
    /// </summary>
    public UpdateOperation<TDocument> ToUpdate(UpdateDefinition<TDocument> update) =>
        new(TypedModel, DisabledFeatures, Target, Filter, update, Document)
        {
            Condition = Condition,
            TrackedOriginal = TrackedOriginal
        };

    /// <inheritdoc/>
    public override BsonDocument? RenderFilter() => Filter?.Render(TypedModel.RenderArgs);

    internal override async ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(run, Filter, cancellationToken);

        return IsSetBased
            ? new BulkWriteDeleteManyModel<TDocument>(Namespace, filter)
            : new BulkWriteDeleteOneModel<TDocument>(Namespace, filter);
    }

    internal override async ValueTask<int> WriteAsync(SaveRun run,
        int index,
        OperationResult[] results,
        CancellationToken cancellationToken)
    {
        var filter = new BsonDocumentFilterDefinition<TDocument>(await WriteFilterDocumentAsync(run, Filter, cancellationToken));
        var collection = TypedModel.MongoCollection;

        if (IsSetBased && TypedModel.Encryption is { SingleDocumentWrites: true })
        {
            long deleted = 0;
            foreach (var id in await MatchingIdsAsync(run, filter.Document, cancellationToken))
            {
                deleted += (await collection.DeleteOneAsync(run.Session, ById(id), cancellationToken: cancellationToken)).DeletedCount;
            }

            results[index] = OperationResult.Of(0, 0, 0, deleted);
            return 1;
        }

        var written = IsSetBased
            ? await collection.DeleteManyAsync(run.Session, filter, cancellationToken: cancellationToken)
            : await collection.DeleteOneAsync(run.Session, filter, cancellationToken: cancellationToken);

        results[index] = OperationResult.Of(0, 0, 0, written.DeletedCount);
        return 1;
    }
}
