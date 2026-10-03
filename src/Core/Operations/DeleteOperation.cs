using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Deletes the document with a key, or every document matching a filter.</summary>
public sealed class DeleteOperation<TDocument> : VaultOperation<TDocument>
{
    internal DeleteOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        KeyTarget<TDocument>? target,
        Expression<Func<TDocument, bool>>? filter,
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

    /// <summary>The documents to delete, or <see langword="null"/> when the operation targets a key.</summary>
    public Expression<Func<TDocument, bool>>? Filter { get; }

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

    internal override async ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(run, Filter, cancellationToken);

        return IsSetBased
            ? new BulkWriteDeleteManyModel<TDocument>(Namespace, filter)
            : new BulkWriteDeleteOneModel<TDocument>(Namespace, filter);
    }
}
