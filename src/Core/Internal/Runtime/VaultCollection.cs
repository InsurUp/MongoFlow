using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoDB.Driver.Search;

namespace MongoFlow;

/// <summary>
/// A vault collection, typed and untyped: the untyped members check what they're handed and call the typed ones, so both
/// queue the same operations.
/// </summary>
internal class VaultCollection<TDocument>(
    VaultRuntime runtime,
    CollectionModel<TDocument> model,
    FeatureSet disabled) : IVaultCollection<TDocument>, IVaultCollection
{
    // The index a search uses unless it names one, as the server's.
    private const string DefaultIndex = "default";

    protected VaultRuntime Runtime { get; } = runtime;

    protected FeatureSet Disabled { get; } = disabled;

    public Type DocumentType => model.DocumentType;

    public Type? KeyType => model.KeyType;

    public string PropertyName => model.PropertyName;

    public IMongoCollection<TDocument> MongoCollection => model.MongoCollection;

    public IVaultCollection<TDocument> Without(FeatureKey feature) => View(Disabled.With(feature));

    IVaultCollection IVaultCollection.Without(FeatureKey feature) => View(Disabled.With(feature));

    public async ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(QueryAsync), filter);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        // Loaded now, so the query, which may run synchronously, only has to encrypt.
        if (model.Encryption is { } encryption)
        {
            await encryption.EnsureReadyAsync(cancellationToken);
        }

        var queryable = model.Queryable(session);

        var query = filter is null ? queryable : queryable.Where(filter);
        var provider = new VaultQueryProvider<TDocument>(query.GetMongoQueryProvider(),
            model,
            Tracker);

        return new VaultQueryable<TDocument, TDocument>(query, provider);
    }

    public async ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var queryFilter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(FindAsync), queryFilter);

        var combined = FilterExpressions.Combine(filter, queryFilter);

        return await FindInSessionAsync(combined is null ? FilterDefinition<TDocument>.Empty : combined, cancellationToken);
    }

    public async ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var queryFilter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(FindAsync), queryFilter);

        return await FindInSessionAsync(queryFilter is null ? filter : filter & queryFilter, cancellationToken);
    }

    public async ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        LogReading(nameof(AggregateAsync), filter);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        var aggregate = session is null ? model.MongoCollection.Aggregate() : model.MongoCollection.Aggregate(session);

        return filter is null ? aggregate : aggregate.Match(await model.ReadFilterAsync(filter, cancellationToken));
    }

    public async ValueTask<IAggregateFluent<TDocument>> SearchAsync(SearchDefinition<TDocument> search,
        SearchOptions<TDocument>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        ThrowIfEncrypted();

        var index = options?.IndexName ?? DefaultIndex;
        var (split, _) = await SplitAsync(nameof(SearchAsync), index, cancellationToken);

        if (split.Residual is not null &&
            (options?.ReturnStoredSource == true || options?.CountOptions is not null ||
             SearchStages.IsFacet(search.Render(model.RenderArgs), out _)))
        {
            throw new NotSupportedException(
                $"A search of {model.Namespace.CollectionName} in {index} can't count, facet or return stored source: the " +
                $"query filters on {string.Join(", ", split.PostFiltered)} are matched after the search, so what it " +
                "counted would include documents they hide. Map the fields in the index to compare exactly, such as a " +
                "string as SearchField.Token(), and compare them in ways a search can, without regular expressions or $expr.");
        }

        var filtered = split.HasClauses ? new FilteredSearchDefinition<TDocument>(search, split) : search;
        var aggregate = model.MongoCollection.Aggregate().Search(filtered, options ?? new SearchOptions<TDocument>());

        return split.Residual is null ? aggregate : aggregate.Match(new BsonDocumentFilterDefinition<TDocument>(split.Residual));
    }

    public async ValueTask<SearchMetaResult> SearchMetaAsync(SearchDefinition<TDocument> search,
        string? indexName = null,
        SearchCountOptions? count = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        ThrowIfEncrypted();

        var index = indexName ?? DefaultIndex;
        var (split, matchesNothing) = await SplitAsync(nameof(SearchMetaAsync), index, cancellationToken);

        if (matchesNothing)
        {
            return Nothing(search, count);
        }

        if (split.Residual is not null)
        {
            throw new NotSupportedException(
                $"SearchMetaAsync can't count the documents of {model.Namespace.CollectionName} the query filters show: " +
                $"{index} can't filter on {string.Join(", ", split.PostFiltered)} as they do, and nothing can follow " +
                "$searchMeta to filter after it. Map the fields in the index to compare exactly, such as a string as " +
                "SearchField.Token(), and compare them in ways a search can, without regular expressions or $expr.");
        }

        return await model.MongoCollection.Aggregate()
            .SearchMeta(split.HasClauses ? new FilteredSearchDefinition<TDocument>(search, split) : search, index, count)
            .FirstAsync(cancellationToken);
    }

    public async ValueTask<IAggregateFluent<TDocument>> VectorSearchAsync(FieldDefinition<TDocument> field,
        QueryVector vector,
        int limit,
        VectorSearchOptions<TDocument>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ThrowIfEncrypted();

        var index = options?.IndexName ?? DefaultIndex;
        var split = await VectorSplitAsync(index, cancellationToken);

        if (split.Residual is not null && options?.ReturnStoredSource == true)
        {
            throw new NotSupportedException(
                $"A vector search of {model.Namespace.CollectionName} in {index} can't return stored source: the query " +
                $"filters on {string.Join(", ", split.PostFiltered)} are matched after the search, against the stored " +
                "documents. Declare the fields as filter fields, compared with plain values.");
        }

        // Rendered for this search alone, so it's changed in place: a copy would lose the query vector, which the driver
        // holds in an array of its own.
        var stage = PipelineStageDefinitionBuilder.VectorSearch(field, vector, limit, options).Render(model.RenderArgs).Document;
        var body = stage["$vectorSearch"].AsBsonDocument;
        body.Set("index", index);

        if (split.Filter is { } filter)
        {
            body["filter"] = body.GetValue("filter", BsonNull.Value) is BsonDocument own ? FilterDocuments.And(own, filter) : filter;
        }

        var aggregate = model.MongoCollection.Aggregate()
            .AppendStage(new BsonDocumentPipelineStageDefinition<TDocument, TDocument>(stage, model.MongoCollection.DocumentSerializer));

        return split.Residual is null ? aggregate : aggregate.Match(new BsonDocumentFilterDefinition<TDocument>(split.Residual));
    }

    public async ValueTask<SearchFilters<TDocument>> SearchFiltersAsync(string indexName = DefaultIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ThrowIfEncrypted();

        var (split, matchesNothing) = await SplitAsync(nameof(SearchFiltersAsync), indexName, cancellationToken);
        return new SearchFilters<TDocument>(split, matchesNothing);
    }

    public void Add(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new InsertOperation<TDocument>(model, Disabled, document) { Tracker = Tracker });
    }

    void IVaultCollection.Add(object document) => Add(DocumentOf(document));

    public void AddRange(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(document);
        }
    }

    void IVaultCollection.AddRange(IEnumerable<object> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(DocumentOf(document));
        }
    }

    public void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);

        QueueUpdateMany(filter, update);
    }

    void IVaultCollection.UpdateMany(BsonDocument filter, BsonValue update)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueUpdateMany(new BsonDocumentFilterDefinition<TDocument>(filter), UpdateOf(update));
    }

    public void DeleteMany(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueDeleteMany(filter);
    }

    void IVaultCollection.DeleteMany(BsonDocument filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        QueueDeleteMany(new BsonDocumentFilterDefinition<TDocument>(filter));
    }

    /// <summary>
    /// Tracks what the reads return, and what the saves insert or replace, when this is a keyed view that tracks changes;
    /// otherwise <see langword="null"/>.
    /// </summary>
    protected virtual IDocumentTracker<TDocument>? Tracker => null;

    /// <summary>A view of the collection with <paramref name="disabled"/> switched off, keyed if this one is.</summary>
    protected virtual VaultCollection<TDocument> View(FeatureSet disabled) => new(Runtime, model, disabled);

    /// <summary><paramref name="document"/>, handed to an untyped member, as a <typeparamref name="TDocument"/>.</summary>
    protected TDocument DocumentOf(object document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document is TDocument typed
            ? typed
            : throw new ArgumentException(
                $"{model.Namespace.CollectionName} holds {typeof(TDocument).Name} documents, so it can't take one of " +
                $"type {document.GetType().Name}.",
                nameof(document));
    }

    /// <summary>
    /// <paramref name="update"/>, handed to an untyped member, as an update definition: a document of update operators, or
    /// an array of pipeline stages.
    /// </summary>
    protected static UpdateDefinition<TDocument> UpdateOf(BsonValue update) => UpdateDocuments.Of<TDocument>(update);

    /// <summary>What a find returns: wrapped, to track the documents it returns, when the reads track changes.</summary>
    protected IFindFluent<TDocument, TDocument> Tracked(IFindFluent<TDocument, TDocument> find) =>
        Tracker is { } tracker ? new TrackingFindFluent<TDocument>(find, tracker) : find;

    /// <summary>Finds with <paramref name="filter"/>, in the scope's open transaction if there is one.</summary>
    private async ValueTask<IFindFluent<TDocument, TDocument>> FindInSessionAsync(FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken)
    {
        var session = await Runtime.GetSessionAsync(cancellationToken);
        filter = await model.ReadFilterAsync(filter, cancellationToken);

        return Tracked(session is null ? model.MongoCollection.Find(filter) : model.MongoCollection.Find(session, filter));
    }

    private void QueueUpdateMany(FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update) =>
        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, null, filter, update, default));

    private void QueueDeleteMany(FilterDefinition<TDocument> filter) =>
        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, null, filter, default));

    /// <summary>
    /// The query filters split for a search of <paramref name="index"/>, and whether they match nothing, in which case
    /// they're all left to match after it, and the index isn't read.
    /// </summary>
    private async ValueTask<(SearchFilterSplit Split, bool MatchesNothing)> SplitAsync(string method,
        string index,
        CancellationToken cancellationToken)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        var split = SearchFilterSplit.None;
        var matchesNothing = filter?.Body is ConstantExpression { Value: false };

        if (filter is not null)
        {
            var rendered = model.Render(filter);
            if (matchesNothing)
            {
                split = new SearchFilterSplit([], [], rendered, []);
            }
            else
            {
                var mapping = await model.Search.MappingAsync(index, SearchIndexType.Search, cancellationToken);
                split = SearchFilterTranslator.Split(rendered, mapping);
                model.Search.NotePostFiltered(index, split.PostFiltered);
            }
        }

        var clauses = split.HasClauses
            ? new BsonDocument { { "filter", new BsonArray(split.Filter) }, { "mustNot", new BsonArray(split.MustNot) } }
            : null;
        LogSearching(method, index, filter, clauses, split.Residual);
        return (split, matchesNothing);
    }

    /// <summary>The query filters split for a vector search of <paramref name="index"/>.</summary>
    private async ValueTask<VectorFilterSplit> VectorSplitAsync(string index,
        CancellationToken cancellationToken)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        var split = VectorFilterSplit.None;

        if (filter is not null)
        {
            var rendered = model.Render(filter);
            if (filter.Body is ConstantExpression { Value: false })
            {
                split = new VectorFilterSplit(null, rendered, []);
            }
            else
            {
                var mapping = await model.Search.MappingAsync(index, SearchIndexType.VectorSearch, cancellationToken);
                split = VectorFilterTranslator.Split(rendered, mapping);
                model.Search.NotePostFiltered(index, split.PostFiltered);
            }
        }

        LogSearching(nameof(VectorSearchAsync), index, filter, split.Filter, split.Residual);
        return split;
    }

    /// <summary>
    /// What <c>$searchMeta</c> returns when the query filters match nothing: no documents counted, and the facets the search
    /// asks for, with no buckets.
    /// </summary>
    private SearchMetaResult Nothing(SearchDefinition<TDocument> search,
        SearchCountOptions? count)
    {
        var facets = SearchStages.IsFacet(search.Render(model.RenderArgs), out var facet)
            ? (facet.GetValue("facets", BsonNull.Value) as BsonDocument ?? []).Names
            .ToDictionary(name => name, _ => new SearchMetaFacetResult([]))
            : null;

        var total = count?.Type == SearchCountType.Total;
        return new SearchMetaResult(new SearchMetaCountResult(total ? null : 0, total ? 0 : null), facets);
    }

    /// <exception cref="NotSupportedException">The collection has encrypted members.</exception>
    private void ThrowIfEncrypted()
    {
        if (model.Encryption is not null)
        {
            throw new NotSupportedException(
                $"{model.Namespace.CollectionName} has encrypted members, and MongoDB Search can't search a collection with " +
                "Queryable Encryption.");
        }
    }

    private void LogSearching(string method,
        string index,
        Expression<Func<TDocument, bool>>? queryFilter,
        BsonDocument? clauses,
        BsonDocument? postFilter)
    {
        var log = Runtime.Model.Logs.Query;
        if (log.IsEnabled(LogLevel.Trace))
        {
            log.Searching(method,
                model.Namespace.CollectionName,
                index,
                queryFilter?.ToString() ?? "none",
                clauses?.ToJson() ?? "none",
                postFilter?.ToJson() ?? "none");
        }
    }

    private void LogReading(string method,
        Expression<Func<TDocument, bool>>? queryFilter)
    {
        var log = Runtime.Model.Logs.Query;
        if (log.IsEnabled(LogLevel.Trace))
        {
            log.Reading(method, model.Namespace.CollectionName, queryFilter?.ToString() ?? "none");
        }
    }
}
