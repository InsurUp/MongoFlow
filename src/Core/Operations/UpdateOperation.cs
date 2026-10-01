using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Applies an update definition to the document with a key, or to every document matching a filter.</summary>
public sealed class UpdateOperation<TDocument> : VaultOperation<TDocument>
{
    internal UpdateOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        KeyTarget<TDocument>? target,
        Expression<Func<TDocument, bool>>? filter,
        UpdateDefinition<TDocument> update,
        TDocument? document)
        : base(model, disabledFeatures, target)
    {
        Filter = filter;
        Update = update;
        Document = document;
    }

    public override OperationKind Kind => OperationKind.Update;

    public override bool IsSetBased => Filter is not null;

    /// <summary>The key of the one document to update, or <see langword="null"/> when the operation is set-based.</summary>
    public object? Key => Target?.Key;

    /// <summary>The documents to update, or <see langword="null"/> when the operation targets a key.</summary>
    public Expression<Func<TDocument, bool>>? Filter { get; }

    /// <summary>
    /// The changes to make. Built-in features may add to it during <see cref="VaultInterceptor.SavingAsync"/>: the
    /// concurrency token adds its increment.
    /// </summary>
    public UpdateDefinition<TDocument> Update { get; internal set; }

    /// <summary>
    /// The same target and document with a different definition, keeping the features switched off. Replace the operation
    /// with it to add changes of your own, such as <c>Builders&lt;T&gt;.Update.Combine(operation.Update, stamp)</c>.
    /// </summary>
    public UpdateOperation<TDocument> WithUpdate(UpdateDefinition<TDocument> update) =>
        new(TypedModel, DisabledFeatures, Target, Filter, update, Document) { Condition = Condition };

    internal override async ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(run, Filter, cancellationToken);

        return IsSetBased
            ? new BulkWriteUpdateManyModel<TDocument>(Namespace, filter, Update)
            : new BulkWriteUpdateOneModel<TDocument>(Namespace, filter, Update);
    }
}
