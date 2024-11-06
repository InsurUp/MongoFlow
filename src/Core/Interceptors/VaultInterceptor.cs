namespace MongoFlow;

public abstract class VaultInterceptor
{
    public virtual ValueTask SavingChangesAsync(
        VaultInterceptorContext context,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public virtual ValueTask SavedChangesAsync(
        VaultInterceptorContext context,
        int result,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public virtual Task SaveChangesFailedAsync(Exception exception,
        VaultInterceptorContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
