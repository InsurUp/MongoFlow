namespace MongoFlow;

public abstract class VaultInterceptor
{
    public virtual ValueTask SavingChangesAsync(
        VaultInterceptorContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public virtual ValueTask SavedChangesAsync(
        VaultInterceptorContext context,
        int result,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public virtual ValueTask SaveChangesFailedAsync(Exception exception,
        VaultInterceptorContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
