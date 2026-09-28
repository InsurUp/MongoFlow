namespace MongoFlow;

public interface IMongoGlobalTransactionManager
{
    IMongoVaultTransaction? CurrentTransaction { get; }
    Task<IMongoVaultTransaction> BeginAsync(CancellationToken cancellationToken = default);
}