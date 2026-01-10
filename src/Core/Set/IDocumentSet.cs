using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public interface IDocumentSet<TDocument>
{
    IMongoCollection<TDocument> Collection { get; }

    // Query methods
    IFindFluent<TDocument, TDocument> Find(Expression<Func<TDocument, bool>> filter);
    IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter);
    IFindFluent<TDocument, TDocument> Find();
    IQueryable<TDocument> AsQueryable();
    IAggregateFluent<TDocument> Aggregate();

    // CRUD operations
    void Add(TDocument document);
    void AddRange(IEnumerable<TDocument> documents);
    void Delete(TDocument document);
    Task DeleteByKeyAsync(object key, CancellationToken cancellationToken = default);
    void Replace(TDocument document);
    void Update(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update);
    void UpdateByKey(object key, UpdateDefinition<TDocument> update);

    // GetByKey
    Task<TDocument?> GetByKeyAsync(object key, IClientSessionHandle? session, CancellationToken cancellationToken = default);
    Task<TDocument?> GetByKeyAsync(object key, CancellationToken cancellationToken = default);

    // Disable methods
    IDocumentSet<TDocument> DisableQueryFilters(params string[] names);
    IDocumentSet<TDocument> DisableAllQueryFilters();
    IDocumentSet<TDocument> DisableInterceptors(params string[] names);
    IDocumentSet<TDocument> DisableAllInterceptors();

    // Key filter
    Expression<Func<TDocument, bool>> BuildKeyFilter(object key);
}
