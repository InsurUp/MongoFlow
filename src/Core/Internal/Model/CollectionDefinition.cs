using MongoDB.Driver;

namespace MongoFlow;

internal sealed record CollectionDefinition<TDocument>(
    string PropertyName,
    Type? KeyType,
    IMongoCollection<TDocument> Collection,
    IReadOnlyList<QueryFilterEntry<TDocument>> Filters);
