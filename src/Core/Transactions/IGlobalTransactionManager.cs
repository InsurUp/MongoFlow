namespace MongoFlow;

public interface IGlobalTransactionManager
{
    IVaultTransaction? CurrentTransaction { get; }
    Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default);
}