using MongoDB.Driver;

namespace MongoFlow;

internal sealed class MongoGlobalTransaction : IMongoVaultTransaction
{
    public MongoGlobalTransaction(IClientSessionHandle session)
    {
        if (session.IsInTransaction)
        {
            throw new InvalidOperationException("MongoVaultTransaction can only be created from an inactive transaction.");
        }

        session.StartTransaction();

        Session = session;
    }

    public void Dispose()
    {
        Session.Dispose();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Session.CommitTransactionAsync(cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await Session.AbortTransactionAsync(cancellationToken);
    }

    public IClientSessionHandle Session { get; }
}
