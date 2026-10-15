using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Reads by filter in one call, as <see cref="IVaultCollection{TDocument, TKey}.GetByKeyAsync"/> is for a key: each is
/// <c>FindAsync(filter)</c> followed by the driver's operator of the same name.
/// </summary>
/// <remarks>
/// <para>
/// The query filters, the scope's transaction, views such as <c>Without</c> and <c>WithNoTracking</c>, and change tracking
/// apply as they do to <c>FindAsync</c>. The documents a read returns are tracked; <c>AnyAsync</c>, which reads only
/// <c>_id</c>, and <c>CountAsync</c> track nothing.
/// </para>
/// <para>
/// Nothing is sorted, so which document is first is the server's choice. Sorting, limits, projections, joins and grouping
/// stay on <c>FindAsync</c>, <c>QueryAsync</c> and <c>AggregateAsync</c>.
/// </para>
/// </remarks>
public static class VaultCollectionReadExtensions
{
    /// <summary>The documents matching <paramref name="filter"/> and the query filters.</summary>
    public static async Task<List<TDocument>> ToListAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.ToListAsync(cancellationToken);
    }

    /// <summary>The documents matching <paramref name="filter"/>, built with the driver, and the query filters.</summary>
    public static async Task<List<TDocument>> ToListAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.ToListAsync(cancellationToken);
    }

    /// <summary>A document matching <paramref name="filter"/> and the query filters, or the default if there's none.</summary>
    public static async Task<TDocument?> FirstOrDefaultAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// A document matching <paramref name="filter"/>, built with the driver, and the query filters, or the default if
    /// there's none.
    /// </summary>
    public static async Task<TDocument?> FirstOrDefaultAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>A document matching <paramref name="filter"/> and the query filters.</summary>
    /// <exception cref="InvalidOperationException">There's none.</exception>
    public static async Task<TDocument> FirstAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.FirstAsync(cancellationToken);
    }

    /// <summary>A document matching <paramref name="filter"/>, built with the driver, and the query filters.</summary>
    /// <exception cref="InvalidOperationException">There's none.</exception>
    public static async Task<TDocument> FirstAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.FirstAsync(cancellationToken);
    }

    /// <summary>
    /// The one document matching <paramref name="filter"/> and the query filters, or the default if there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">There's more than one.</exception>
    public static async Task<TDocument?> SingleOrDefaultAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The one document matching <paramref name="filter"/>, built with the driver, and the query filters, or the default
    /// if there's none.
    /// </summary>
    /// <exception cref="InvalidOperationException">There's more than one.</exception>
    public static async Task<TDocument?> SingleOrDefaultAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>The one document matching <paramref name="filter"/> and the query filters.</summary>
    /// <exception cref="InvalidOperationException">There's none, or more than one.</exception>
    public static async Task<TDocument> SingleAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.SingleAsync(cancellationToken);
    }

    /// <summary>The one document matching <paramref name="filter"/>, built with the driver, and the query filters.</summary>
    /// <exception cref="InvalidOperationException">There's none, or more than one.</exception>
    public static async Task<TDocument> SingleAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.SingleAsync(cancellationToken);
    }

    /// <summary>Whether a document matches <paramref name="filter"/> and the query filters. Only its <c>_id</c> is read.</summary>
    public static async Task<bool> AnyAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Whether a document matches <paramref name="filter"/>, built with the driver, and the query filters. Only its
    /// <c>_id</c> is read.
    /// </summary>
    public static async Task<bool> AnyAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.AnyAsync(cancellationToken);
    }

    /// <summary>How many documents match <paramref name="filter"/> and the query filters, with <c>CountDocumentsAsync</c>.</summary>
    public static async Task<long> CountAsync<TDocument>(this IVaultCollection<TDocument> collection,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.CountDocumentsAsync(cancellationToken);
    }

    /// <summary>
    /// How many documents match <paramref name="filter"/>, built with the driver, and the query filters, with
    /// <c>CountDocumentsAsync</c>.
    /// </summary>
    public static async Task<long> CountAsync<TDocument>(this IVaultCollection<TDocument> collection,
        FilterDefinition<TDocument> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var find = await collection.FindAsync(filter, cancellationToken);
        return await find.CountDocumentsAsync(cancellationToken);
    }
}
