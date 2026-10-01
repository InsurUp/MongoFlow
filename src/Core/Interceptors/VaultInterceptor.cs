namespace MongoFlow;

/// <summary>Runs code at each step of a vault's save.</summary>
/// <remarks>
/// Register it on a vault to see operations on every collection, or on a collection to see only that collection's. One
/// registered from <see cref="IVaultFeature.Configure{TVault}"/> belongs to the feature and is skipped for operations
/// queued with the feature switched off.
/// <para>
/// <see cref="SavingAsync"/> runs in registration order. The hooks after the write, <see cref="SavedAsync"/>,
/// <see cref="CommittedAsync"/> and <see cref="FailedAsync"/>, run in reverse, so the interceptor that runs last before
/// the write runs first after it. The built-in concurrency token's runs last before the write, so it guards the writes as
/// they'll be sent, and first after it, so a rejected write fails the save before other interceptors see it.
/// </para>
/// <para>
/// Registered by type, an interceptor is created from the request's services once per vault instance, when a save first
/// needs it, and disposed with the request's scope if it implements <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/>. Registered as an instance, it's shared by every vault instance and request, so it's used
/// from many threads at once, and MongoFlow never disposes it. Either way, keep what belongs to one save in
/// <see cref="SaveContext.Items"/> rather than in fields: a vault instance can save more than once.
/// </para>
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
