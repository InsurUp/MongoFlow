namespace MongoFlow;

/// <summary>Runs code at each step of a vault's save.</summary>
/// <remarks>
/// Register it on a vault to see operations on every collection, or on a collection to see only that collection's. One
/// registered from <see cref="IVaultFeature.Configure{TVault}"/> belongs to the feature and is skipped for operations
/// queued with the feature switched off. Interceptors run in registration order; the built-in concurrency token's runs
/// after all of them, so it guards the writes as they'll be sent.
/// </remarks>
public abstract class VaultInterceptor
{
    /// <summary>Before anything is written. Change operations or documents here, or throw to cancel the save.</summary>
    public virtual ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// After the bulk write, before the commit. Results are available, and saves of other vaults made here join the
    /// same transaction.
    /// </summary>
    public virtual ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// After the commit, including the commit of an outer transaction the save joined. For side effects outside the
    /// database; a failure here can't undo the save.
    /// </summary>
    public virtual ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// When the save fails at any step, or the transaction it joined fails or is rolled back. The transaction is already
    /// aborted.
    /// </summary>
    public virtual ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
