using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A transaction vaults join. Disposing it without committing rolls it back. Once it ends,
/// <see cref="IVaultTransactions.Current"/> is cleared.
/// </summary>
/// <remarks>A vault on a different client can't join; its save throws.</remarks>
public interface IVaultTransaction : IAsyncDisposable
{
    IClientSessionHandle Session { get; }

    /// <summary>Commits, then runs <see cref="VaultInterceptor.CommittedAsync"/> for every save that joined.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Aborts, then runs <see cref="VaultInterceptor.FailedAsync"/> for every save that joined.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
