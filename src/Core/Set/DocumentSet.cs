using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public sealed class DocumentSet<TDocument>
{
    private readonly MongoVault _vault;
    private readonly DocumentSetConfiguration _documentSetConfiguration;
    private readonly IMongoCollection<TDocument> _collection;
    private readonly DisableContext _queryFilterDisableContext;
    private readonly DisableContext _interceptorDisableContext;

    public DocumentSet(MongoVault vault, 
        DisableContext? queryFilterDisableContext = null,
        DisableContext? interceptorDisableContext = null)
    {
        _vault = vault;
        _queryFilterDisableContext = queryFilterDisableContext ?? DisableContext.Empty;
        _interceptorDisableContext = interceptorDisableContext ?? DisableContext.Empty;
        _documentSetConfiguration = vault.Configuration.GetDocumentSetConfiguration<TDocument>();
        _collection = _vault.GetCollection<TDocument>();
    }

    public IMongoCollection<TDocument> Collection => _collection;

    public IFindFluent<TDocument, TDocument> Find(Expression<Func<TDocument, bool>> filter)
    {
        var transformedFilter = IncludeQueryFilters(filter);
        
        return _vault.CurrentTransaction is not null ?
            _collection.Find(_vault.CurrentTransaction.Session, transformedFilter)
            : _collection.Find(transformedFilter);
    }

    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        var queryFilter = IncludeQueryFilters();
        var finalFilter = queryFilter is not null ? filter & queryFilter : filter;

        return _vault.CurrentTransaction is not null ?
            _collection.Find(_vault.CurrentTransaction.Session, finalFilter)
            : _collection.Find(finalFilter);
    }

    public IFindFluent<TDocument, TDocument> Find()
    {
        var filter = IncludeQueryFilters() ?? Builders<TDocument>.Filter.Empty;

        return _vault.CurrentTransaction is not null ?
            _collection.Find(_vault.CurrentTransaction.Session, filter)
            : _collection.Find(filter);
    }

    public IQueryable<TDocument> AsQueryable()
    {
        var filter = IncludeQueryFilters();

        var queryable = _vault.CurrentTransaction is not null ?
            _collection.AsQueryable(_vault.CurrentTransaction.Session)
            : _collection.AsQueryable();

        return filter is not null ? queryable.Where(filter) : queryable;
    }

    public IAggregateFluent<TDocument> Aggregate()
    {
        var filter = IncludeQueryFilters() ?? Builders<TDocument>.Filter.Empty;
        
        var aggregate = _vault.CurrentTransaction is not null ?
            _collection.Aggregate(_vault.CurrentTransaction.Session)
            : _collection.Aggregate();

        return aggregate.Match(filter);
    }

    public void Add(TDocument document)
    {
        var operation = new AddOperation<TDocument>(document, _interceptorDisableContext);

        _vault.AddOperation(operation);
    }

    public void Delete(TDocument document)
    {
        var filter = BuildKeyFilter(document);

        var operation = new DeleteOperation<TDocument>(filter, document, _interceptorDisableContext);

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

        var operation = new ReplaceOperation<TDocument>(filter, document, _interceptorDisableContext);

        _vault.AddOperation(operation);
    }

    public void Update(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        var operation = new UpdateOperation<TDocument>(filter, update, _interceptorDisableContext);

        _vault.AddOperation(operation);
    }

    public void UpdateByKey(object key, UpdateDefinition<TDocument> update)
    {
        var filter = BuildKeyFilter(key);

        var operation = new UpdateOperation<TDocument>(filter, update, _interceptorDisableContext);

        _vault.AddOperation(operation);
    }

    public async Task<TDocument?> GetByKeyAsync(object key,
        IClientSessionHandle? session,
        CancellationToken cancellationToken = default)
    {
        session ??= _vault.CurrentTransaction?.Session;
        
        var filter = BuildKeyFilter(key);
        var find = session is not null ?
            _collection.Find(session, filter)
            : _collection.Find(filter);

        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<TDocument?> GetByKeyAsync(object key,
        CancellationToken cancellationToken = default)
    {
        return GetByKeyAsync(key, null, cancellationToken);
    }

    public DocumentSet<TDocument> DisableQueryFilters(params string[] names)
    {
        var newDisableContext = _queryFilterDisableContext.Disable(names);
        
        return new DocumentSet<TDocument>(_vault, newDisableContext, _interceptorDisableContext);
    }

    public DocumentSet<TDocument> DisableAllQueryFilters()
    {
        return new DocumentSet<TDocument>(_vault, DisableContext.All, _interceptorDisableContext);
    }
    
    public DocumentSet<TDocument> DisableInterceptors(params string[] names)
    {
        var newDisableContext = _interceptorDisableContext.Disable(names);
        
        return new DocumentSet<TDocument>(_vault, _queryFilterDisableContext, newDisableContext);
    }
    
    public DocumentSet<TDocument> DisableAllInterceptors()
    {
        return new DocumentSet<TDocument>(_vault, _queryFilterDisableContext, DisableContext.All);
    }

    private Expression<Func<TDocument, bool>>? IncludeQueryFilters(Expression<Func<TDocument, bool>>? expression = null)
    {
        if (_queryFilterDisableContext.AllDisabled)
        {
            return expression;
        }
        
        var queryFilters = _documentSetConfiguration.QueryFilterDefinitions
            .Where(x => !_queryFilterDisableContext.DisabledItems.Contains(x.Name))
            .Select(x => x.Get(_vault.ServiceProvider))
            .ToArray();

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

        return IncludeQueryFilters(keyFilter)!;
    }

    private Expression<Func<TDocument, bool>> BuildKeyFilter(TDocument document)
    {
        var keyExpression = _documentSetConfiguration.GetKeyExpression<TDocument>();
        var key = keyExpression.Compile().Invoke(document);

        return BuildKeyFilter(key);
    }

    public void AddRange(IEnumerable<TDocument> documents)
    {
        var operation = new AddRangeOperation<TDocument>(documents, _interceptorDisableContext);

        _vault.AddOperation(operation);
    }
}
