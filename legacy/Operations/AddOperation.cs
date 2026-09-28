namespace MongoFlow;

public sealed class AddOperation<TDocument> : VaultOperation
{
    private readonly TDocument _document;

    public AddOperation(TDocument document,
        DisableContext interceptorDisableContext)
    {
        _document = document;
        InterceptorDisableContext = interceptorDisableContext;
    }

    public override Type DocumentType => typeof(TDocument);

    public override object? CurrentDocument => _document;

    public override object? OldDocument => null;

    public override OperationType OperationType => OperationType.Add;
    
    public override DisableContext InterceptorDisableContext { get; }

    internal override async Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();
        await collection.InsertOneAsync(context.Session, _document, cancellationToken: cancellationToken);

        return 1;
    }

    public override bool To(OperationType operationType, out VaultOperation? operation)
    {
        operation = operationType switch
        {
            OperationType.Add => this,
            _ => null
        };

        return operation is not null;
    }
}
