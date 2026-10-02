using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

/// <param name="tracking">Whether the documents its reads return are tracked, for the vault's save to write their changes.</param>
internal sealed class KeyedVaultCollection<TDocument, TKey>(
    VaultRuntime runtime,
    KeyedCollectionModel<TDocument, TKey> model,
    FeatureSet disabled,
    bool tracking) : VaultCollection<TDocument>(runtime, model, disabled), IVaultCollection<TDocument, TKey>,
    IDocumentTracker<TDocument>
{
    public override IVaultCollection<TDocument> Without(FeatureKey feature) => WithoutKeyed(feature);

    IVaultCollection<TDocument, TKey> IVaultCollection<TDocument, TKey>.Without(FeatureKey feature) => WithoutKeyed(feature);

    public IVaultCollection<TDocument, TKey> WithTracking() =>
        new KeyedVaultCollection<TDocument, TKey>(Runtime, model, Disabled, tracking: true);

    public IVaultCollection<TDocument, TKey> WithNoTracking() =>
        new KeyedVaultCollection<TDocument, TKey>(Runtime, model, Disabled, tracking: false);

    public async Task<TDocument?> GetByKeyAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        FilterDefinition<TDocument> filter = new BsonDocumentFilterDefinition<TDocument>(model.Key.Match(key));
        var queryFilter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);

        var log = Runtime.Model.Logs.Query;
        if (log.IsEnabled(LogLevel.Trace))
        {
            log.ReadingByKey(model.Namespace.CollectionName, key, queryFilter?.ToString() ?? "none");
        }

        if (queryFilter is not null)
        {
            filter &= queryFilter;
        }

        var session = await Runtime.GetSessionAsync(cancellationToken);
        var find = session is null ? model.MongoCollection.Find(filter) : model.MongoCollection.Find(session, filter);

        return await Tracked(find).FirstOrDefaultAsync(cancellationToken);
    }

    public void Replace(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new ReplaceOperation<TDocument>(model, Disabled, TargetOf(document), document));
    }

    public void Delete(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, TargetOf(document), null, document));
    }

    public void DeleteByKey(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, model.Target(key), null, default));
    }

    public void UpdateByKey(TKey key, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, model.Target(key), null, update, default));
    }

    public void Update(TDocument document, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, TargetOf(document), null, update, document));
    }

    /// <summary>
    /// Keeps <paramref name="document"/>, with its BSON as read, for the vault's save to compare. One without a key can't
    /// be updated by key, so it isn't tracked.
    /// </summary>
    public void Track(TDocument document)
    {
        if (model.Key.Get(document) is not { } key)
        {
            return;
        }

        var snapshot = BsonSnapshot.Of(model.MongoCollection.DocumentSerializer, document);
        Runtime.Tracker.Track(new TrackedDocument<TDocument, TKey>(model, Disabled, document, key, snapshot));
    }

    protected override IQueryable<TDocument> Tracked(IQueryable<TDocument> query) =>
        tracking
            ? new TrackingQueryable<TDocument, TDocument>(query,
                new TrackingQueryProvider<TDocument>(query.GetMongoQueryProvider(), this))
            : query;

    protected override IFindFluent<TDocument, TDocument> Tracked(IFindFluent<TDocument, TDocument> find) =>
        tracking ? new TrackingFindFluent<TDocument>(find, this) : find;

    private KeyedVaultCollection<TDocument, TKey> WithoutKeyed(FeatureKey feature) =>
        new(Runtime, model, Disabled.With(feature), tracking);

    private KeyTarget<TDocument> TargetOf(TDocument document) =>
        model.Key.Get(document) is { } key
            ? model.Target(key)
            : throw new ArgumentException(
                $"The document's key is null, so {model.Namespace.CollectionName} can't find it.",
                nameof(document));
}
