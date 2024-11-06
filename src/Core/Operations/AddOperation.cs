namespace MongoFlow;

public sealed class AddOperation<TDocument> : VaultOperation
{
    private readonly TDocument _document;

    public AddOperation(TDocument document)
    {
        _document = document;
    }

    public override Type DocumentType => typeof(TDocument);

    public override object? CurrentDocument => _document;

    public override object? OldDocument => null;

    public override OperationType OperationType => OperationType.Add;

    internal override Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();
        return collection.InsertOneAsync(context.Session, _document, cancellationToken: cancellationToken)
            .ContinueWith(_ => 1, cancellationToken);
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
