using System.Linq.Expressions;

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
    /// A filter the stored document must match as well, such as a concurrency token still having the value that was read.
    /// When it's set and the write matches nothing, the save fails with <see cref="ConcurrencyException"/>. Only for
    /// operations that target one document by key.
    /// </summary>
    internal Expression<Func<TDocument, bool>>? Condition { get; set; }

    internal override bool HasCondition => Condition is not null;

    internal override Task<bool> TargetExistsAsync(SaveRun run,
        CancellationToken cancellationToken) =>
        TypedModel.TargetExistsAsync(this, run, cancellationToken);

    private protected override object? GetDocument() => Document;
}
