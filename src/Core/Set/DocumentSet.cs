using System.Linq.Expressions;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow;

public sealed class DocumentSet<TDocument>
{
    private readonly MongoVault _vault;
    private readonly bool _ignoreQueryFilter;
    private readonly DocumentSetConfiguration _documentSetConfiguration;
    private readonly IMongoCollection<TDocument> _collection;

    public DocumentSet(MongoVault vault, bool ignoreQueryFilter = false)
    {
        _vault = vault;
        _ignoreQueryFilter = ignoreQueryFilter;
        _documentSetConfiguration = vault.Configuration.GetDocumentSetConfiguration<TDocument>();
        _collection = _vault.GetCollection<TDocument>();
    }

    public IMongoCollection<TDocument> Collection => _collection;

    public IFindFluent<TDocument, TDocument> Find(Expression<Func<TDocument, bool>> filter)
    {
        return _collection.Find(TransformQueryFilterExpression(filter));
    }

    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        var queryFilter = TransformQueryFilterExpression();
        var finalFilter = queryFilter is not null ? filter & queryFilter : filter;

        return _collection.Find(finalFilter);
    }

    public IFindFluent<TDocument, TDocument> Find()
    {
        var filter = TransformQueryFilterExpression();

        return _collection.Find(filter ?? Builders<TDocument>.Filter.Empty);
    }

    public IMongoQueryable<TDocument> AsQueryable()
    {
        var filter = TransformQueryFilterExpression();

        var queryable = _collection.AsQueryable();

        return filter is not null ? queryable.Where(filter) : queryable;
    }

    public IAggregateFluent<TDocument> Aggregate()
    {
        var filter = TransformQueryFilterExpression() ?? Builders<TDocument>.Filter.Empty;

        return _collection.Aggregate().Match(filter);
    }

    public void Add(TDocument document)
    {
        var operation = new AddOperation<TDocument>(document);

        _vault.AddOperation(operation);
    }

    public void Delete(TDocument document)
    {
        var filter = BuildKeyFilter(document);

        var operation = new DeleteOperation<TDocument>(filter, document);

        _vault.AddOperation(operation);
    }

    public async Task DeleteByKeyAsync(object key, CancellationToken cancellationToken = default)
    {
        var document = await GetByKeyAsync(key, cancellationToken);

        if (document is not null)
        {
            Delete(document);
        }
    }

    public void Replace(TDocument document)
    {
        var filter = BuildKeyFilter(document);

        var operation = new ReplaceOperation<TDocument>(filter, document);

        _vault.AddOperation(operation);
    }

    public void Update(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        var operation = new UpdateOperation<TDocument>(filter, update);

        _vault.AddOperation(operation);
    }

    public void UpdateByKey(object key, UpdateDefinition<TDocument> update)
    {
        var filter = BuildKeyFilter(key);

        var operation = new UpdateOperation<TDocument>(filter, update);

        _vault.AddOperation(operation);
    }

    public async Task<TDocument?> GetByKeyAsync(object key, CancellationToken cancellationToken = default)
    {
        var filter = BuildKeyFilter(key);

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public DocumentSet<TDocument> IgnoreQueryFilter()
    {
        return new DocumentSet<TDocument>(_vault, true);
    }

    private Expression<Func<TDocument, bool>>? TransformQueryFilterExpression(Expression<Func<TDocument, bool>>? expression = null)
    {
        if (_ignoreQueryFilter)
        {
            return expression;
        }

        var queryFilters = _documentSetConfiguration.BuildQueryFilters(_vault.ServiceProvider);

        if (queryFilters.Length == 0)
        {
            return expression;
        }

        var filters = expression is not null ?
            [expression, ..queryFilters]
            : queryFilters;

        return filters.CombineAnd<TDocument>();
    }

    private Expression<Func<TDocument, bool>> BuildKeyFilter(object key)
    {
        var keyFilter = _documentSetConfiguration.BuildKeyFilterExpression<TDocument>(key);

        return TransformQueryFilterExpression(keyFilter)!;
    }

    private Expression<Func<TDocument, bool>> BuildKeyFilter(TDocument document)
    {
        var keyExpression = _documentSetConfiguration.GetKeyExpression<TDocument>();
        var key = keyExpression.Compile().Invoke(document);

        return BuildKeyFilter(key);
    }

    public void AddRange(IEnumerable<TDocument> documents)
    {
        var operation = new AddRangeOperation<TDocument>(documents);

        _vault.AddOperation(operation);
    }
}
