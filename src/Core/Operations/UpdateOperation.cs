using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Applies an update definition to the document with a key, or to every document matching a filter.</summary>
public sealed class UpdateOperation<TDocument> : VaultOperation<TDocument>
{
    internal UpdateOperation(CollectionModel<TDocument> model,
        IReadOnlySet<FeatureKey> disabledFeatures,
        KeyTarget<TDocument>? target,
        Expression<Func<TDocument, bool>>? filter,
        UpdateDefinition<TDocument> update)
        : base(model, disabledFeatures, target)
    {
        if ((target is null) == (filter is null))
        {
            throw new ArgumentException("An update targets either a key or a filter.");
        }

        Filter = filter;
        Update = update;
    }

    public override OperationKind Kind => OperationKind.Update;

    public override bool IsSetBased => Filter is not null;

    /// <summary>The key of the one document to update, or <see langword="null"/> when the operation is set-based.</summary>
    public object? Key => Target?.Key;

    /// <summary>The documents to update, or <see langword="null"/> when the operation targets a key.</summary>
    public Expression<Func<TDocument, bool>>? Filter { get; }

    public UpdateDefinition<TDocument> Update { get; }

    /// <summary>
    /// The same target with a different definition, keeping the features switched off. Replace the operation with it to
    /// add changes of your own, such as <c>Builders&lt;T&gt;.Update.Combine(operation.Update, stamp)</c>.
    /// </summary>
    public UpdateOperation<TDocument> WithUpdate(UpdateDefinition<TDocument> update) =>
        new(TypedModel, DisabledFeatures, Target, Filter, update);

    internal override ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        TypedModel.CreateWriteModelAsync(this, run, cancellationToken);
}
