using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class DocumentSetConfiguration
{
    public DocumentSetConfiguration(DocumentProperty key,
        string name,
        IReadOnlyCollection<IQueryFilterDefinition> queryFilterDefinitions)
    {
        Key = key;
        Name = name;
        QueryFilterDefinitions = queryFilterDefinitions;
    }

    public DocumentProperty Key { get; }
    public string Name { get; }
    public IReadOnlyCollection<IQueryFilterDefinition> QueryFilterDefinitions { get; }

    internal LambdaExpression[] BuildQueryFilters(IServiceProvider serviceProvider)
    {
        return QueryFilterDefinitions.Select(x => x.Get(serviceProvider)).ToArray();
    }

    public Expression<Func<TDocument, object>> GetKeyExpression<TDocument>()
    {
        var key = KeyExpressionCache<TDocument>.Get(Key);

        return key;
    }

    public Expression<Func<TDocument, bool>> BuildKeyFilterExpression<TDocument>(object key)
    {
        var keyExpression = GetKeyExpression<TDocument>();
        return KeyFilterExpressionBuilder.CreateKeyFilter(key, keyExpression);
    }
}
