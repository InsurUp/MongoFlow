using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A collection's search indexes, as searches need them: those it declares, and any other, listed from the server the
/// first time it's searched and kept, to learn which fields its query filters can be filtered on in the index.
/// </summary>
internal sealed class CollectionSearch
{
    private readonly string _collection;
    private readonly Dictionary<string, SearchIndexDeclaration> _declared;
    private readonly Func<string, CancellationToken, Task<BsonDocument?>> _list;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<SearchIndexMapping>>> _listed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Index, string Field), bool> _warned = new();

    /// <param name="collection">The collection's name, for logs and errors.</param>
    /// <param name="declarations">The indexes the collection declares.</param>
    /// <param name="list">Lists the index of a name, as <c>$listSearchIndexes</c> shows it, or <see langword="null"/>.</param>
    /// <param name="logger">The search logger.</param>
    public CollectionSearch(string collection,
        IReadOnlyList<SearchIndexDeclaration> declarations,
        Func<string, CancellationToken, Task<BsonDocument?>> list,
        ILogger logger)
    {
        _collection = collection;
        Declarations = declarations;
        _declared = declarations.ToDictionary(declaration => declaration.Name, StringComparer.Ordinal);
        _list = list;
        _logger = logger;
    }

    /// <summary>The indexes the collection declares, which the migrator creates and keeps up to date.</summary>
    public IReadOnlyList<SearchIndexDeclaration> Declarations { get; }

    /// <summary>What the index named <paramref name="index"/> maps, checked to be of <paramref name="type"/>.</summary>
    /// <exception cref="InvalidOperationException">There's no such index, or it's of the other type.</exception>
    public async ValueTask<SearchIndexMapping> MappingAsync(string index,
        SearchIndexType type,
        CancellationToken cancellationToken)
    {
        var mapping = _declared.TryGetValue(index, out var declaration)
            ? declaration.Mapping
            : await ListedAsync(index, cancellationToken);

        if (mapping.Type != type)
        {
            throw new InvalidOperationException(type == SearchIndexType.VectorSearch
                ? $"{index}, of {_collection}, is a search index: search it with SearchAsync, or name a vector search index."
                : $"{index}, of {_collection}, is a vector search index: search it with VectorSearchAsync, or name a search index.");
        }

        return mapping;
    }

    /// <summary>Warns, once per index and field, of the fields a search's query filters are matched on after it.</summary>
    public void NotePostFiltered(string index,
        IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
        {
            if (_warned.TryAdd((index, field), true))
            {
                _logger.FilterFieldNotPreFiltered(_collection, index, field);
            }
        }
    }

    // Listed once, by whichever search comes first, without its token so another search waiting for it isn't canceled with
    // it; a listing that fails is tried again by the next search.
    private async Task<SearchIndexMapping> ListedAsync(string index,
        CancellationToken cancellationToken)
    {
        var listing = _listed.GetOrAdd(index, name => new Lazy<Task<SearchIndexMapping>>(() => ListAsync(name)));
        try
        {
            return await listing.Value.WaitAsync(cancellationToken);
        }
        catch when (listing.Value.IsFaulted)
        {
            _listed.TryRemove(KeyValuePair.Create(index, listing));
            throw;
        }
    }

    private async Task<SearchIndexMapping> ListAsync(string index)
    {
        var listed = await _list(index, CancellationToken.None) ??
                     throw new InvalidOperationException(
                         $"{_collection} has no search index named {index}, so a search of it would find nothing. Declare " +
                         "it on the collection with SearchIndex or VectorSearchIndex, which IVaultMigrator creates, or create it.");

        var type = SearchIndexSync.TypeOf(listed);
        var definition = listed.GetValue("latestDefinition", BsonNull.Value) as BsonDocument ?? [];

        _logger.IndexListed(SearchIndexSync.TypeName(type), index, _collection);
        return SearchIndexMapping.Parse(definition, type);
    }
}
