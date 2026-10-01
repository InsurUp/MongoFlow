using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed record CollectionDefinition<TDocument>(
    PropertyInfo Property,
    Type? KeyType,
    IMongoCollection<TDocument> Collection,
    IReadOnlyList<QueryFilterEntry<TDocument>> Filters);
