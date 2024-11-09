using MongoDB.Driver;

namespace MongoFlow;

public interface IVaultTransaction : IDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
    IClientSessionHandle Session { get; }
}
