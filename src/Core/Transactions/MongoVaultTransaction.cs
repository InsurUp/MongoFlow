using MongoDB.Driver;

namespace MongoFlow;

internal sealed class MongoVaultTransaction : IMongoVaultTransaction
{
    private readonly MongoVault _vault;

    public MongoVaultTransaction(MongoVault vault,
        IClientSessionHandle session)
    {
        if (session.IsInTransaction)
        {
            throw new InvalidOperationException("MongoVaultTransaction can only be created from an inactive transaction.");
        }

        session.StartTransaction();

        _vault = vault;
        Session = session;
    }

    public void Dispose()
    {
        Session.Dispose();
        _vault.ClearTransaction();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Session.CommitTransactionAsync(cancellationToken);
        _vault.ClearTransaction();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await Session.AbortTransactionAsync(cancellationToken);
        _vault.ClearTransaction();
    }

    public IClientSessionHandle Session { get; }
}
