using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Searches of a vault collection by a member, rather than a field definition.</summary>
public static class VaultCollectionSearchExtensions
{
    /// <inheritdoc cref="IVaultCollection{TDocument}.VectorSearchAsync"/>
    /// <param name="collection">The collection to search.</param>
    /// <param name="field">The member holding the embeddings, such as <c>x =&gt; x.Embedding</c>.</param>
    /// <param name="vector">The vector to find the nearest of.</param>
    /// <param name="limit">How many documents to return.</param>
    /// <param name="options">The search's options, such as its index, <c>default</c> without, and its candidates.</param>
    /// <param name="cancellationToken">Cancels resolving the query filters and, the first time, reading the index.</param>
    public static ValueTask<IAggregateFluent<TDocument>> VectorSearchAsync<TDocument, TField>(
        this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, TField>> field,
        QueryVector vector,
        int limit,
        VectorSearchOptions<TDocument>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(field);

        return collection.VectorSearchAsync(new ExpressionFieldDefinition<TDocument, TField>(field), vector, limit, options,
            cancellationToken);
    }
}
