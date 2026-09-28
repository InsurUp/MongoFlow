namespace MongoFlow;

public sealed class AddRangeOperation<TDocument> : AddRangeOperation
{
    private readonly IReadOnlyList<TDocument> _documents;

    public AddRangeOperation(IEnumerable<TDocument> documents,
        DisableContext interceptorDisableContext)
    {
        // Materialised once, here. Held as the sequence it was handed, a lazy one — AddRange(xs.Select(
        // ToDocument)) — is enumerated again by every interceptor that walks CurrentDocuments and once
        // more to execute, producing a fresh set of objects each time. Multi-tenancy then stamps one
        // set, the next interceptor stamps another, and a third is what actually gets written.
        _documents = documents as IReadOnlyList<TDocument> ?? documents.ToList();
        InterceptorDisableContext = interceptorDisableContext;
    }

    public override Type DocumentType => typeof(TDocument);

    public override DisableContext InterceptorDisableContext { get; }

    internal override async Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();
        await collection.InsertManyAsync(context.Session, _documents, cancellationToken: cancellationToken);
        
        return _documents.Count;
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
