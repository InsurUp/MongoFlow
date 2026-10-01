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

    internal override object? TargetKey => Target?.Key;

    /// <summary>
    /// A filter the stored document must match as well, or <see langword="null"/>: such as <c>{ Version: 3 }</c>, which the
    /// concurrency token adds so the write applies only while the stored token has the value that was read.
    /// </summary>
    /// <remarks>
    /// A write whose condition fails matches nothing, and the save goes on. Whoever added the condition checks
    /// <see cref="VaultOperation.Result"/> in <see cref="VaultInterceptor.SavedAsync"/> and decides what that means, as the
    /// concurrency token does by throwing <see cref="ConcurrencyException"/>.
    /// </remarks>
    public BsonDocument? Condition { get; internal set; }

    /// <summary>
    /// Adds <paramref name="condition"/> to <see cref="Condition"/>, joined with AND, so interceptors' conditions don't
    /// replace each other. It's rendered with the collection's serializers, such as
    /// <c>Builders&lt;Order&gt;.Filter.Eq(x =&gt; x.Status, "open")</c>. Add conditions during
    /// <see cref="VaultInterceptor.SavingAsync"/>, before the write.
    /// </summary>
    /// <exception cref="InvalidOperationException">The operation is an insert, which matches no stored document.</exception>
    public void AddCondition(FilterDefinition<TDocument> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        AddCondition(condition.Render(TypedModel.RenderArgs));
    }

    /// <inheritdoc cref="AddCondition(FilterDefinition{TDocument})"/>
    internal void AddCondition(BsonDocument condition)
    {
        if (Kind == OperationKind.Insert)
        {
            throw new InvalidOperationException("An insert matches no stored document, so it can't take a condition.");
        }

        Condition = FilterDocuments.And(Condition, condition);
    }

    /// <summary>Whether the operation's target matches a stored document, with <paramref name="condition"/> if there's one.</summary>
    internal async Task<bool> TargetExistsAsync(SaveRun run,
        BsonDocument? condition,
        CancellationToken cancellationToken)
    {
        var filter = await MatchAsync(run, null, condition, cancellationToken);

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
