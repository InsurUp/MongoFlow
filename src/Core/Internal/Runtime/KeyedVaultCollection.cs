using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <param name="tracking">Whether the documents its reads return are tracked, for the vault's save to write their changes.</param>
internal sealed class KeyedVaultCollection<TDocument, TKey>(
    VaultRuntime runtime,
    KeyedCollectionModel<TDocument, TKey> model,
    FeatureSet disabled,
    bool tracking) : VaultCollection<TDocument>(runtime, model, disabled), IVaultCollection<TDocument, TKey>,
    IKeyedVaultCollection, IDocumentTracker<TDocument>
{
    IVaultCollection<TDocument, TKey> IVaultCollection<TDocument, TKey>.Without(FeatureKey feature) =>
        View(Disabled.With(feature));

    IKeyedVaultCollection IKeyedVaultCollection.Without(FeatureKey feature) => View(Disabled.With(feature));

    public IVaultCollection<TDocument, TKey> WithTracking() => Tracking(true);

    IKeyedVaultCollection IKeyedVaultCollection.WithTracking() => Tracking(true);

    public IVaultCollection<TDocument, TKey> WithNoTracking() => Tracking(false);

    IKeyedVaultCollection IKeyedVaultCollection.WithNoTracking() => Tracking(false);

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

    async Task<object?> IKeyedVaultCollection.GetByKeyAsync(object key, CancellationToken cancellationToken) =>
        await GetByKeyAsync(KeyOf(key), cancellationToken);

    public void Replace(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new ReplaceOperation<TDocument>(model, Disabled, TargetOf(document), document));
    }

    void IKeyedVaultCollection.Replace(object document) => Replace(DocumentOf(document));

    public void Delete(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, TargetOf(document), null, document));
    }

    void IKeyedVaultCollection.Delete(object document) => Delete(DocumentOf(document));

    public void DeleteByKey(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, model.Target(key), null, default));
    }

    void IKeyedVaultCollection.DeleteByKey(object key) => DeleteByKey(KeyOf(key));

    public void UpdateByKey(TKey key, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, model.Target(key), null, update, default));
    }

    void IKeyedVaultCollection.UpdateByKey(object key, BsonValue update) => UpdateByKey(KeyOf(key), UpdateOf(update));

    public void Update(TDocument document, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, TargetOf(document), null, update, document));
    }

    void IKeyedVaultCollection.Update(object document, BsonValue update) => Update(DocumentOf(document), UpdateOf(update));

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

    protected override IDocumentTracker<TDocument>? Tracker => tracking ? this : null;

    protected override KeyedVaultCollection<TDocument, TKey> View(FeatureSet disabled) => new(Runtime, model, disabled, tracking);

    private KeyedVaultCollection<TDocument, TKey> Tracking(bool tracks) => new(Runtime, model, Disabled, tracks);

    /// <summary><paramref name="key"/>, handed to an untyped member, as a <typeparamref name="TKey"/>.</summary>
    private TKey KeyOf(object key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key is TKey typed
            ? typed
            : throw new ArgumentException(
                $"{model.Namespace.CollectionName} is keyed by {typeof(TKey).Name}, so it can't take a key of type " +
                $"{key.GetType().Name}.",
                nameof(key));
    }

    private KeyTarget<TDocument> TargetOf(TDocument document) =>
        model.Key.Get(document) is { } key
            ? model.Target(key)
            : throw new ArgumentException(
                $"The document's key is null, so {model.Namespace.CollectionName} can't find it.",
                nameof(document));
}
