using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A search index a collection declares, rendered: what the migrator creates, and what searches filter in.</summary>
internal sealed record SearchIndexDeclaration(string Name,
    SearchIndexType Type,
    BsonDocument Definition)
{
    /// <summary>What the index maps, for translating query filters.</summary>
    public SearchIndexMapping Mapping { get; } = SearchIndexMapping.Parse(Definition, Type);

    /// <summary>The type as <c>$listSearchIndexes</c> names it.</summary>
    public string TypeName => SearchIndexSync.TypeName(Type);
}
