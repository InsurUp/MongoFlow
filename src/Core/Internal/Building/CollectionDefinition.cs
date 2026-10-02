using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed record CollectionDefinition<TDocument>(
    PropertyInfo Property,
    Type? KeyType,
    IMongoCollection<TDocument> Collection,
    ImmutableArray<QueryFilterEntry<TDocument>> Filters,
    ImmutableArray<(LambdaExpression Field, string Feature)> IndexedFields);
