using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>A search definition, filtered with a query filter's clauses when it's rendered.</summary>
internal sealed class FilteredSearchDefinition<TDocument>(SearchDefinition<TDocument> search,
    SearchFilterSplit split) : SearchDefinition<TDocument>
{
    public override BsonDocument Render(RenderArgs<TDocument> args) => SearchStages.Filter(search.Render(args), split);
}
