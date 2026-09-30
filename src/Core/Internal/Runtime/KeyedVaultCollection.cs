using MongoDB.Driver;

namespace MongoFlow;

internal sealed class KeyedVaultCollection<TDocument, TKey>(
    VaultRuntime runtime,
    KeyedCollectionModel<TDocument, TKey> model,
    IReadOnlySet<FeatureKey> disabled) : VaultCollection<TDocument>(runtime, model, disabled), IVaultCollection<TDocument, TKey>
{
    public override IVaultCollection<TDocument> Without(FeatureKey feature) => WithoutKeyed(feature);

    IVaultCollection<TDocument, TKey> IVaultCollection<TDocument, TKey>.Without(FeatureKey feature) => WithoutKeyed(feature);

    public async Task<TDocument?> GetByKeyAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var filter = FilterExpressions.Combine(
            model.Key.Filter(key),
            await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken))!;

        var session = await Runtime.GetSessionAsync(cancellationToken);
        var find = session is null ? model.MongoCollection.Find(filter) : model.MongoCollection.Find(session, filter);

        return await find.FirstOrDefaultAsync(cancellationToken);
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

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, model.Target(key), null, update));
    }

    private KeyedVaultCollection<TDocument, TKey> WithoutKeyed(FeatureKey feature) =>
        new(Runtime, model, With(Disabled, feature));

    private KeyTarget<TDocument> TargetOf(TDocument document) =>
        model.Key.Get(document) is { } key
            ? model.Target(key)
            : throw new ArgumentException(
                $"The document's key is null, so {model.Namespace.CollectionName} can't find it.",
                nameof(document));
}
