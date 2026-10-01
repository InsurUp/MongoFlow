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

    internal override Task<bool> TargetExistsAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        TypedModel.TargetExistsAsync(this, run, cancellationToken);

    private protected override object? GetDocument() => Document;
}
