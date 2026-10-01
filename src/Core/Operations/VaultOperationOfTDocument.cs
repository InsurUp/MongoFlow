using MongoDB.Bson;

namespace MongoFlow;

public abstract class VaultOperation<TDocument> : VaultOperation
{
    private protected VaultOperation(CollectionModel<TDocument> model,
        IReadOnlySet<FeatureKey> disabledFeatures,
        KeyTarget<TDocument>? target)
        : base(model, model.Namespace, disabledFeatures)
    {
        TypedModel = model;
        Target = target;
    }

    /// <inheritdoc cref="VaultOperation.Document"/>
    public new TDocument? Document { get; private protected init; }

    internal CollectionModel<TDocument> TypedModel { get; }

    /// <summary>The one document the operation targets by key, or <see langword="null"/> for inserts and set-based operations.</summary>
    internal KeyTarget<TDocument>? Target { get; }

    /// <summary>
    /// A filter the stored document must match as well, such as <c>{ Version: 3 }</c> for a concurrency token still having
    /// the value that was read. A write whose condition fails matches nothing; whoever set the condition decides what that
    /// means. Only for operations that target one document by key.
    /// </summary>
    internal BsonDocument? Condition { get; set; }

    private protected override object? GetDocument() => Document;
}
