using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// The one document an operation targets by key. Operations expose the key as <see cref="object"/> for interceptors
/// that handle many collections; this keeps it typed for building the filter.
/// </summary>
internal abstract class KeyTarget<TDocument>
{
    public abstract object Key { get; }

    public abstract BsonDocument Match();
}
