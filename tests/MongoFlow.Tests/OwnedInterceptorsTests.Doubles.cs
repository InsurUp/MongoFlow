namespace MongoFlow.Tests;

// Interceptors disposable each way, and one that isn't, recording how they were disposed.
public partial class OwnedInterceptorsTests
{
    public sealed class Plain : VaultInterceptor;

    public sealed class Disposable(string name, List<string> log) : VaultInterceptor, IDisposable
    {
        public void Dispose() => log.Add($"{name} disposed");
    }

    public sealed class AsyncDisposable(string name, List<string> log) : VaultInterceptor, IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            log.Add($"{name} disposed asynchronously");
            return ValueTask.CompletedTask;
        }
    }

    public sealed class BothDisposable(string name, List<string> log) : VaultInterceptor, IDisposable, IAsyncDisposable
    {
        public void Dispose() => log.Add($"{name} disposed");

        public ValueTask DisposeAsync()
        {
            log.Add($"{name} disposed asynchronously");
            return ValueTask.CompletedTask;
        }
    }
}
