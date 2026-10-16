namespace MongoFlow;

/// <summary>
/// What the vaults of one root provider share, such as the client that encrypts for vaults with the same encryption
/// settings, so they can save in one transaction. Disposed with the provider.
/// </summary>
internal sealed class SharedVaultResources : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<object, object> _resources = [];
    private bool _disposed;

    /// <summary>The resource kept under <paramref name="key"/>, created by <paramref name="create"/> the first time.</summary>
    public T GetOrAdd<T>(object key,
        Func<T> create) where T : class
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_resources.TryGetValue(key, out var resource))
            {
                resource = create();
                _resources.Add(key, resource);
            }

            return (T)resource;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var resource in _resources.Values)
            {
                (resource as IDisposable)?.Dispose();
            }

            _resources.Clear();
        }
    }
}
