using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A transaction vaults join. Disposing it without committing rolls it back. Once it ends,
/// <see cref="IVaultTransactionManager.Current"/> is cleared.
/// </summary>
/// <remarks>
/// <para>A vault on a different client can't join; its save throws.</para>
/// <para>
/// A save that fails after writing rolls the whole transaction back, since MongoDB can't undo part of one: until it's
/// disposed, the transaction fails whatever tries to use it, its commit included. A save that fails before writing, such
/// as in an interceptor's <see cref="VaultInterceptor.SavingAsync"/>, leaves it as it was.
/// </para>
/// </remarks>
public interface IVaultTransaction : IAsyncDisposable
{
    IClientSessionHandle Session { get; }

    /// <summary>Commits, then runs <see cref="VaultInterceptor.CommittedAsync"/> for every save that joined.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Aborts, then runs <see cref="VaultInterceptor.FailedAsync"/> for every save that joined.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
