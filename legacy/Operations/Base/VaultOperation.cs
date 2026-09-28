namespace MongoFlow;

public abstract class VaultOperation
{
    public abstract Type DocumentType { get; }

    public abstract object? CurrentDocument { get; }

    public abstract object? OldDocument { get; }

    public abstract OperationType OperationType { get; }
    
    public abstract DisableContext InterceptorDisableContext { get; }

    internal abstract Task<int> ExecuteAsync(VaultOperationContext context,
        CancellationToken cancellationToken = default);

    public abstract bool To(OperationType operationType, out VaultOperation? operation);
}
