using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Applies an update definition to the document with a key, or to every document matching a filter.</summary>
public sealed class UpdateOperation<TDocument> : VaultOperation<TDocument>
{
    internal UpdateOperation(CollectionModel<TDocument> model,
        FeatureSet disabledFeatures,
        KeyTarget<TDocument>? target,
        FilterDefinition<TDocument>? filter,
        UpdateDefinition<TDocument> update,
        TDocument? document)
        : base(model, disabledFeatures, target)
    {
        Filter = filter;
        Update = update;
        Document = document;
    }

    /// <inheritdoc/>
    public override OperationKind Kind => OperationKind.Update;

    /// <inheritdoc/>
    public override bool IsSetBased => Filter is not null;

    /// <summary>
    /// The documents to update, or <see langword="null"/> when the operation targets a key: the filter it was queued with,
    /// the expression a typed collection was given or the BSON an untyped one was. <see cref="RenderFilter"/> renders
    /// either.
    /// </summary>
    public FilterDefinition<TDocument>? Filter { get; }

    /// <summary>
    /// The changes to make. Built-in features may add to it during <see cref="VaultInterceptor.SavingAsync"/>: the
    /// concurrency token adds its increment.
    /// </summary>
    public UpdateDefinition<TDocument> Update { get; internal set; }

    /// <summary>
    /// The same target, document, condition and original with a different definition, keeping the features switched
    /// off. Replace the operation with it to add changes of your own, such as
    /// <c>Builders&lt;T&gt;.Update.Combine(operation.Update, stamp)</c>.
    /// </summary>
    public UpdateOperation<TDocument> WithUpdate(UpdateDefinition<TDocument> update) =>
        new(TypedModel, DisabledFeatures, Target, Filter, update, Document)
        {
            Condition = Condition,
            TrackedOriginal = TrackedOriginal
        };

    /// <inheritdoc/>
    public override BsonDocument? RenderFilter() => Filter?.Render(TypedModel.RenderArgs);

    /// <inheritdoc/>
    public override BsonValue RenderUpdate() => Update.Render(TypedModel.RenderArgs);

    internal override async ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var filter = await WriteFilterAsync(run, Filter, cancellationToken);

        return IsSetBased
            ? new BulkWriteUpdateManyModel<TDocument>(Namespace, filter, Update)
            : new BulkWriteUpdateOneModel<TDocument>(Namespace, filter, Update);
    }

    internal override async ValueTask<int> WriteAsync(SaveRun run,
        int index,
        OperationResult[] results,
        CancellationToken cancellationToken)
    {
        var filter = new BsonDocumentFilterDefinition<TDocument>(await WriteFilterDocumentAsync(run, Filter, cancellationToken));
        var collection = TypedModel.MongoCollection;

        try
        {
            if (IsSetBased && TypedModel.Encryption is { SingleDocumentWrites: true })
            {
                long matched = 0;
                long modified = 0;

                foreach (var id in await MatchingIdsAsync(run, filter.Document, cancellationToken))
                {
                    // Each document gets its own encrypted values: the server refuses one sent twice.
                    var each = await collection.UpdateOneAsync(run.Session, ById(id), await SentUpdateAsync(cancellationToken),
                        cancellationToken: cancellationToken);
                    matched += each.MatchedCount;
                    modified += each.ModifiedCount;
                }

                results[index] = OperationResult.Of(0, matched, modified, 0);
                return 1;
            }

            var update = await SentUpdateAsync(cancellationToken);
            var written = IsSetBased
                ? await collection.UpdateManyAsync(run.Session, filter, update, cancellationToken: cancellationToken)
                : await collection.UpdateOneAsync(run.Session, filter, update, cancellationToken: cancellationToken);

            results[index] = OperationResult.Of(0, written.MatchedCount, written.ModifiedCount, 0);
            return 1;
        }
        catch (MongoWriteException exception) when (IsDuplicateKey(exception))
        {
            throw new DuplicateKeyException(this, exception);
        }
    }

    /// <summary><see cref="Update"/> as it's sent: with the values it sets encrypted members to encrypted.</summary>
    private async ValueTask<UpdateDefinition<TDocument>> SentUpdateAsync(CancellationToken cancellationToken) =>
        TypedModel.Encryption is { } encryption
            ? UpdateDocuments.Of<TDocument>(await encryption.EncryptUpdateAsync(RenderUpdate(), cancellationToken))
            : Update;
}
