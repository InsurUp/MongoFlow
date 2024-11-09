using MongoDB.Driver;

namespace MongoFlow;

internal sealed class MongoGlobalTransactionManager : IMongoGlobalTransactionManager, IDisposable
{
    private readonly MongoClient _mongoClient;
    private readonly SemaphoreSlim _semaphore;

    public MongoGlobalTransactionManager(MongoClient mongoClient)
    {
        _mongoClient = mongoClient;
        _semaphore = new SemaphoreSlim(1, 1);
    }
    
    public IMongoVaultTransaction? CurrentTransaction { get; private set; }
    public async Task<IMongoVaultTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (CurrentTransaction != null)
            {
                throw new InvalidOperationException("Transaction already started.");
            }

            var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
            CurrentTransaction = new MongoGlobalTransaction(session);
            return CurrentTransaction;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _mongoClient.Dispose();
        _semaphore.Dispose();
        CurrentTransaction?.Dispose();
    }
}