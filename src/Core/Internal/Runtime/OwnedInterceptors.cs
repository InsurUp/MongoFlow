namespace MongoFlow;

/// <summary>
/// The disposable interceptors created from a scope's services, disposed with the scope, newest first. DI doesn't track
/// what <c>ActivatorUtilities</c> creates, so this scoped service does.
/// </summary>
/// <remarks>Disposed synchronously, it fails like DI does for an interceptor that only implements <see cref="IAsyncDisposable"/>.</remarks>
internal sealed class OwnedInterceptors : IAsyncDisposable, IDisposable
{
    // A scope's saves run one after another, but one can still be creating an interceptor as the scope is disposed.
    private readonly Lock _lock = new();
    private List<VaultInterceptor>? _created;

    public void Track(VaultInterceptor interceptor)
    {
        if (interceptor is not (IDisposable or IAsyncDisposable))
        {
            return;
        }

        lock (_lock)
        {
            (_created ??= []).Add(interceptor);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var interceptor in Take())
        {
            if (interceptor is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                ((IDisposable)interceptor).Dispose();
            }
        }
    }

    public void Dispose()
    {
        List<VaultInterceptor>? asyncOnly = null;

        foreach (var interceptor in Take())
        {
            if (interceptor is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else
            {
                (asyncOnly ??= []).Add(interceptor);
            }
        }

        if (asyncOnly is not null)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", asyncOnly.Select(interceptor => interceptor.GetType().Name))} can only be disposed " +
                "asynchronously. Dispose of the scope with DisposeAsync.");
        }
    }

    private List<VaultInterceptor> Take()
    {
        lock (_lock)
        {
            var created = _created ?? [];
            _created = null;
            created.Reverse();

            return created;
        }
    }
}
