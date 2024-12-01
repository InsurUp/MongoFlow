namespace MongoFlow;

public interface IVaultOperation
{
    Type DocumentType { get; }
    
    VaultOperationType OperationType { get; }
    
    object[] CachedDocuments { get; }
    
    ValueTask<object[]> FetchDocumentsAsync(IClientSessionHandle session,
        CancellationToken cancellationToken = default);
    
    Task<int> ExecuteAsync(IClientSessionHandle session,
        CancellationToken cancellationToken = default);
    
    bool TryConvert(VaultOperationType operationType, out IVaultOperation? operation);
}
