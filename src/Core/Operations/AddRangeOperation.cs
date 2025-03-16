namespace MongoFlow;

public sealed class AddRangeOperation<TDocument> : AddRangeOperation
{
    private readonly IEnumerable<TDocument> _documents;

    public AddRangeOperation(IEnumerable<TDocument> documents,
        DisableContext interceptorDisableContext)
    {
        _documents = documents;
        InterceptorDisableContext = interceptorDisableContext;
    }

    public override Type DocumentType => typeof(TDocument);

    public override DisableContext InterceptorDisableContext { get; }

    internal override async Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();
        await collection.InsertManyAsync(context.Session, _documents, cancellationToken: cancellationToken);
        
        return _documents.TryGetNonEnumeratedCount(out var count) ? count : _documents.Count();
    }

    public override IEnumerable<object> CurrentDocuments => _documents.OfType<object>();
}

public abstract class AddRangeOperation : VaultOperation
{
    public override object? CurrentDocument => null;

    public override object? OldDocument => null;

    public override OperationType OperationType => OperationType.AddRange;

    public override bool To(OperationType operationType, out VaultOperation? operation)
    {
        operation = operationType switch
        {
            OperationType.AddRange => this,
            _ => null
        };

        return operation is not null;
    }

    public abstract IEnumerable<object> CurrentDocuments { get; }
}
