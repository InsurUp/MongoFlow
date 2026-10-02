using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A tracked document of a keyed collection, with the key it was read with and the features switched off on the view
/// that read it, which its update keeps off: a document read with soft delete off is updated with it off.
/// </summary>
internal sealed class TrackedDocument<TDocument, TKey>(KeyedCollectionModel<TDocument, TKey> model,
    FeatureSet disabled,
    TDocument document,
    TKey key,
    BsonSnapshot snapshot) : TrackedDocument(document!, snapshot)
{
    public override BsonSnapshot Serialize() => BsonSnapshot.Of(model.MongoCollection.DocumentSerializer, document);

    public override VaultOperation? DetectChange()
    {
        var current = Serialize();
        try
        {
            if (BsonDiff.Compare(Snapshot.Span, current.Span) is not { } changes)
            {
                return null;
            }

            if (changes.Touches(model.Key.FieldNames))
            {
                throw new InvalidOperationException(
                    $"The key of a tracked {typeof(TDocument).Name} in {model.Namespace.CollectionName} changed from {key} to " +
                    $"{model.Key.Get(document)}. A document's key can't change: delete the document and add it with the new key.");
            }

            var target = model.Target(key);
            if (changes.NeedsReplace)
            {
                return new ReplaceOperation<TDocument>(model, disabled, target, document);
            }

            var update = new BsonDocumentUpdateDefinition<TDocument>(changes.ToUpdate(current.ToBsonDocument()));

            return new UpdateOperation<TDocument>(model, disabled, target, filter: null, update, document);
        }
        finally
        {
            current.Return();
        }
    }
}
