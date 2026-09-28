using MongoDB.Driver;

namespace MongoFlow;

internal sealed class MongoGlobalTransaction : IMongoVaultTransaction
{
    private readonly MongoGlobalTransactionManager _manager;

    public MongoGlobalTransaction(IClientSessionHandle session, MongoGlobalTransactionManager manager)
    {
        if (session.IsInTransaction)
        {
            throw new InvalidOperationException("MongoVaultTransaction can only be created from an inactive transaction.");
        }

        _manager = manager;

        session.StartTransaction();

        Session = session;
    }

    public void Dispose()
    {
        Session.Dispose();
        _manager.ClearTransaction();
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
