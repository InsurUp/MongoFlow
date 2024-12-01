namespace MongoFlow;

internal sealed class AddOperation<TDocument> : VaultOperationBase<TDocument>
{
    private readonly MongoVault _vault;
    private readonly TDocument[] _documents;

    public AddOperation(MongoVault vault,
        TDocument[] documents) : base(vault, documents)
    {
        _vault = vault;
        _documents = documents;
    }
    
    public AddOperation(MongoVault vault, TDocument document) : this(vault, [document])
    {
    }

    public override VaultOperationType OperationType => VaultOperationType.Add;
    
    public override Task<int> ExecuteAsync(IClientSessionHandle session, CancellationToken cancellationToken = default)
    {
        if (_documents.Length == 0)
        {
            return Task.FromResult(0);
        }
        
        var collection = _vault.GetCollection<TDocument>();
        
        if (_documents.Length == 1)
        {
            return collection.InsertOneAsync(session, _documents[0], cancellationToken: cancellationToken)
                .ContinueWith(_ => 1, cancellationToken);
        }
        
        return collection.InsertManyAsync(session, _documents, cancellationToken: cancellationToken)
            .ContinueWith(_ => _documents.Length, cancellationToken);
    }

    public override bool TryConvert(VaultOperationType operationType, out IVaultOperation? operation)
    {
        operation = operationType switch
        {
            VaultOperationType.Add => this,
            _ => null
        };

        return operation is not null;
    }
}
