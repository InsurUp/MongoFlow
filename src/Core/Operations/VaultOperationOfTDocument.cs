using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

public abstract class VaultOperation<TDocument> : VaultOperation
{
    private protected VaultOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
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

    /// <summary>Whether the operation's target, without its condition, matches a stored document.</summary>
    internal async Task<bool> TargetExistsAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await MatchAsync(run, null, null, cancellationToken);

        return await TypedModel.MongoCollection.Find(run.Session, filter).Limit(1).AnyAsync(cancellationToken);
    }

    /// <summary>What the write matches: its key, or <paramref name="filter"/>, with its query filters and condition.</summary>
    private protected ValueTask<FilterDefinition<TDocument>> WriteFilterAsync(SaveRun run,
        Expression<Func<TDocument, bool>>? filter,
        CancellationToken cancellationToken) =>
        MatchAsync(run, filter, Condition, cancellationToken);

    private protected override object? GetDocument() => Document;

    /// <summary>
    /// The operation's key or <paramref name="filter"/>, joined with the query filters it was queued under and
    /// <paramref name="condition"/>. A key is matched as BSON and the query filters are rendered once per save, so a write
    /// by key needs no LINQ translation.
    /// </summary>
    private async ValueTask<FilterDefinition<TDocument>> MatchAsync(SaveRun run,
        Expression<Func<TDocument, bool>>? filter,
        BsonDocument? condition,
        CancellationToken cancellationToken)
    {
        // An operation targets a key, or is set-based and has a filter.
        var target = Target?.Match() ?? TypedModel.Render(filter!);
        var queryFilter = await run.QueryFilterAsync(TypedModel, DisabledFeatures, cancellationToken);

        return new BsonDocumentFilterDefinition<TDocument>(FilterDocuments.And(target, queryFilter, condition)!);
    }
}
