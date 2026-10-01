using MongoDB.Bson;

namespace MongoFlow;

internal sealed class KeyTarget<TDocument, TKey>(KeyModel<TDocument, TKey> model, TKey key) : KeyTarget<TDocument>
{
    public override object Key => key!;

    public override BsonDocument Match() => model.Match(key);
}
