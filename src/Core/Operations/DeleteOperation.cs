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
        if ((target is null) == (filter is null))
        {
            throw new ArgumentException("A delete targets either a key or a filter.");
        }

        Filter = filter;
        Document = document;
    }

    public override OperationKind Kind => OperationKind.Delete;

    public override bool IsSetBased => Filter is not null;

    /// <summary>The key of the one document to delete, or <see langword="null"/> when the operation is set-based.</summary>
    public object? Key => Target?.Key;

    /// <summary>The documents to delete, or <see langword="null"/> when the operation targets a key.</summary>
    public Expression<Func<TDocument, bool>>? Filter { get; }

    /// <summary>
    /// The same target and document as an update, keeping the features switched off. This is how soft delete turns a
    /// delete into setting a flag.
    /// </summary>
    public UpdateOperation<TDocument> ToUpdate(UpdateDefinition<TDocument> update) =>
        new(TypedModel, DisabledFeatures, Target, Filter, update, Document) { Condition = Condition };

    internal override ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        TypedModel.CreateWriteModelAsync(this, run, cancellationToken);
}
