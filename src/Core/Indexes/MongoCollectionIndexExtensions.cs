using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Indexes managed by hand, such as in a migration on a collection the vault doesn't declare them on, declared as a vault
/// declares its own and kept the same way. Indexes a collection declares with <c>Index</c> need neither: the migrator
/// sets them up, and a migration applies them earlier with <see cref="MigrationContext{TVault}.Collection{TDocument}"/>.
/// </summary>
/// <remarks>
/// They take no session: the server doesn't build indexes in a transaction, so a migration calling them opts out of its
/// own, and what they did stays if it fails afterwards.
/// </remarks>
public static class MongoCollectionIndexExtensions
{
    /// <summary>
    /// Creates the index on <paramref name="keys"/> as <paramref name="configure"/> declares it, or changes its expiry and
    /// visibility in place when it's defined otherwise. It's named as the server names an index, such as
    /// <c>Customer_1</c>, unless <paramref name="configure"/> names it.
    /// </summary>
    /// <returns>Whether it was created, changed or left as it was.</returns>
    /// <exception cref="ArgumentException">The server would refuse the index, such as one without keys.</exception>
    /// <exception cref="InvalidOperationException">
    /// The index differs in what MongoDB can't change in place, such as its keys or whether it's unique: see
    /// <see cref="RecreateIndexAsync{TDocument}"/>.
    /// </exception>
    public static async Task<IndexChange> CreateOrUpdateIndexAsync<TDocument>(this IMongoCollection<TDocument> collection,
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<IIndexBuilder<TDocument>>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var index = IndexBuilder<TDocument>.Declare(collection, keys, configure);
        var name = collection.CollectionNamespace.CollectionName;

        var (_, listed, collation) = await IndexSync.ListAsync(collection.Database, name, cancellationToken);
        var plan = IndexSpecs.Plan(index, listed, collation);
        if (plan.Action == SetupAction.Conflict)
        {
            throw new InvalidOperationException(
                $"The index {index.Name} of {collection.CollectionNamespace.FullName} differs in what MongoDB can't change in " +
                $"place: {string.Join("; ", plan.Conflicts!)}. Recreate it, with RecreateIndexAsync.");
        }

        await IndexSync.ApplyAsync(collection.Database, name, plan, cancellationToken);
        return plan.Action switch
        {
            SetupAction.Create => IndexChange.Created,
            SetupAction.Update => IndexChange.Updated,
            _ => IndexChange.Unchanged
        };
    }

    /// <summary>
    /// Drops the index of its name, and any the server would refuse it beside, on the same keys with the same options,
    /// then creates it as <paramref name="configure"/> declares it: for a change MongoDB can't make in place. Queries go
    /// without it in between.
    /// </summary>
    /// <exception cref="ArgumentException">The server would refuse the index, such as one without keys.</exception>
    public static async Task RecreateIndexAsync<TDocument>(this IMongoCollection<TDocument> collection,
        Func<IndexKeysDefinitionBuilder<TDocument>, IndexKeysDefinition<TDocument>> keys,
        Action<IIndexBuilder<TDocument>>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var index = IndexBuilder<TDocument>.Declare(collection, keys, configure);

        await IndexSync.RecreateAsync(collection.Database, collection.CollectionNamespace.CollectionName, index.Name, index,
            cancellationToken);
    }
}
