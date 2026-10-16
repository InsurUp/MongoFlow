using System.Linq.Expressions;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>A vault collection without a key: it can be queried and written to, but not looked up by key.</summary>
/// <remarks>
/// <para>
/// The server still gives every document an <c>_id</c>, so a keyless document type must ignore it on reads, for example
/// with <c>[BsonIgnoreExtraElements]</c>. That's checked at startup.
/// </para>
/// <para>
/// Reads apply the collection's query filters, and inside a transaction they use its session. Query filters can be
/// asynchronous, so every read starts with one await that resolves them; what it returns is the driver's own type with
/// the filters already applied. A read that only filters, then calls one of the driver's operators, is one call with
/// <see cref="VaultCollectionReadExtensions"/>, such as <c>ToListAsync(filter)</c>.
/// </para>
/// <para>
/// Writes are queued as <see cref="VaultOperation"/>s and sent when the vault is saved. They can be queued from parallel
/// tasks, and reads can run in parallel while no transaction is open; see <see cref="IMongoVault"/>.
/// </para>
/// </remarks>
public interface IVaultCollection<TDocument>
{
    /// <summary>The driver's collection. Reads and writes through it bypass query filters, features and the vault's save.</summary>
    IMongoCollection<TDocument> MongoCollection { get; }

    /// <summary>A view of this collection with a feature switched off for the reads and writes made through it.</summary>
    IVaultCollection<TDocument> Without(FeatureKey feature);

    /// <summary>
    /// A LINQ query of the collection, starting from its query filters. Joined with another collection's query, it joins
    /// only what that query's filters show. The driver's operators in <c>MongoDB.Driver.Linq</c> work on it, except
    /// <c>GetClient()</c>, which needs the driver's own provider.
    /// </summary>
    ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the documents matching <paramref name="filter"/> and the query filters.</summary>
    ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds with a filter built with the driver, such as <c>Builders&lt;T&gt;.Filter.ElemMatch(...)</c>, joined with the
    /// query filters.
    /// </summary>
    ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default);

    /// <summary>An aggregation whose first stage matches the query filters.</summary>
    ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// An aggregation whose first stage is a MongoDB Search of <paramref name="search"/>, filtered with the query filters:
    /// in the index, for the fields it maps to compare exactly, and by a <c>$match</c> right after the search for the rest.
    /// </summary>
    /// <remarks>
    /// Searches see what's committed, once the server's search process has replicated it, a moment after it's written; they
    /// run outside the scope's transaction, which the server doesn't search in, and don't track what they return.
    /// </remarks>
    /// <param name="search">What to search, such as <c>Builders&lt;T&gt;.Search.Text(x =&gt; x.Name, "ada")</c>.</param>
    /// <param name="options">The search's options, such as its index, <c>default</c> without, and its sort.</param>
    /// <param name="cancellationToken">Cancels resolving the query filters and, the first time, reading the index.</param>
    /// <exception cref="InvalidOperationException">The collection has no search index of the name.</exception>
    /// <exception cref="NotSupportedException">
    /// The collection has encrypted members; or the search counts, facets or returns stored source while the index can't
    /// filter on part of the query filters, which would show what they hide.
    /// </exception>
    ValueTask<IAggregateFluent<TDocument>> SearchAsync(SearchDefinition<TDocument> search,
        SearchOptions<TDocument>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a MongoDB Search of <paramref name="search"/> counts, and its facets count, of the documents the query
    /// filters show, with <c>$searchMeta</c>.
    /// </summary>
    /// <param name="search">What to search: an operator, or a <c>facet</c> collector.</param>
    /// <param name="indexName">The search index, <c>default</c> without.</param>
    /// <param name="count">How to count, such as exactly with <c>SearchCountType.Total</c>; a lower bound without.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <exception cref="InvalidOperationException">The collection has no search index of the name.</exception>
    /// <exception cref="NotSupportedException">
    /// The collection has encrypted members; or the index can't filter on part of the query filters, which nothing can
    /// match after <c>$searchMeta</c>.
    /// </exception>
    ValueTask<SearchMetaResult> SearchMetaAsync(SearchDefinition<TDocument> search,
        string? indexName = null,
        SearchCountOptions? count = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An aggregation whose first stage is a MongoDB Vector Search for the <paramref name="limit"/> documents whose
    /// <paramref name="field"/> is nearest <paramref name="vector"/>, filtered with the query filters: in the index, for
    /// its filter fields, and by a <c>$match</c> right after the search for the rest, which can leave fewer than the limit.
    /// </summary>
    /// <remarks>Like <see cref="SearchAsync"/>, it runs outside the scope's transaction and sees what's committed.</remarks>
    /// <param name="field">The field holding the embeddings.</param>
    /// <param name="vector">The vector to find the nearest of.</param>
    /// <param name="limit">How many documents to return.</param>
    /// <param name="options">The search's options, such as its index, <c>default</c> without, and its candidates.</param>
    /// <param name="cancellationToken">Cancels resolving the query filters and, the first time, reading the index.</param>
    /// <exception cref="InvalidOperationException">The collection has no vector search index of the name.</exception>
    /// <exception cref="NotSupportedException">
    /// The collection has encrypted members; or the search returns stored source while the index can't filter on part of
    /// the query filters.
    /// </exception>
    ValueTask<IAggregateFluent<TDocument>> VectorSearchAsync(FieldDefinition<TDocument> field,
        QueryVector vector,
        int limit,
        VectorSearchOptions<TDocument>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The query filters as a search of the index named <paramref name="indexName"/> applies them, for a pipeline built by
    /// hand: the clauses the index filters on, and what's left to match after the search stage.
    /// </summary>
    /// <exception cref="InvalidOperationException">The collection has no search index of the name.</exception>
    /// <exception cref="NotSupportedException">The collection has encrypted members.</exception>
    ValueTask<SearchFilters<TDocument>> SearchFiltersAsync(string indexName = "default",
        CancellationToken cancellationToken = default);

    /// <summary>Queues an insert of <paramref name="document"/>.</summary>
    void Add(TDocument document);

    /// <summary>Queues one insert per document. Adding nothing queues nothing.</summary>
    void AddRange(IEnumerable<TDocument> documents);

    /// <summary>
    /// Queues <paramref name="update"/> of every document matching <paramref name="filter"/> and the query filters.
    /// </summary>
    void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update);

    /// <summary>
    /// Queues a delete of every document matching <paramref name="filter"/> and the query filters; with soft delete, an
    /// update that marks them.
    /// </summary>
    void DeleteMany(Expression<Func<TDocument, bool>> filter);
}
