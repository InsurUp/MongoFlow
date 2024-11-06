using MongoDB.Driver;

namespace MongoFlow;

internal sealed class MongoVaultTransaction : IMongoVaultTransaction
{
    private readonly MongoVault _vault;
    private readonly IClientSessionHandle _session;

    public MongoVaultTransaction(MongoVault vault,
        IClientSessionHandle session)
    {
        if (session.IsInTransaction)
        {
            throw new InvalidOperationException("MongoVaultTransaction can only be created from an inactive transaction.");
        }

        session.StartTransaction();

        _vault = vault;
        _session = session;
    }

    public void Dispose()
    {
        _session.Dispose();
        _vault.ClearTransaction();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _session.CommitTransactionAsync(cancellationToken);
        _vault.ClearTransaction();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _session.AbortTransactionAsync(cancellationToken);
        _vault.ClearTransaction();
    }

    internal IClientSessionHandle GetSession()
    {
        return _session;
    }
}
