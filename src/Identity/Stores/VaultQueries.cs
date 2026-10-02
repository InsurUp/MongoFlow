namespace MongoFlow.Identity;

internal static class VaultQueries
{
    /// <summary>
    /// The query a vault collection's <c>QueryAsync</c> gives, for Identity's synchronous <c>Users</c> and <c>Roles</c>.
    /// Query filters that aren't asynchronous resolve at once; asynchronous ones are waited for.
    /// </summary>
    public static IQueryable<TDocument> Resolve<TDocument>(ValueTask<IQueryable<TDocument>> query) =>
        query.IsCompletedSuccessfully ? query.Result : query.AsTask().GetAwaiter().GetResult();
}
