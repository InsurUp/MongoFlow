namespace MongoFlow;

public sealed class AddRangeOperation<TDocument> : AddRangeOperation
{
    private readonly IEnumerable<TDocument> _documents;

    public AddRangeOperation(IEnumerable<TDocument> documents)
    {
        _documents = documents;
    }

    public override Type DocumentType => typeof(TDocument);

    internal override Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();
        return collection.InsertManyAsync(context.Session, _documents, cancellationToken: cancellationToken)
            .ContinueWith(_ => _documents.TryGetNonEnumeratedCount(out var count) ? count : _documents.Count(), cancellationToken);
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
