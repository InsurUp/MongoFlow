using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public sealed class DeleteOperation<TDocument> : VaultOperation
{
    private readonly Expression<Func<TDocument, bool>> _filter;
    private TDocument? _document;

    public DeleteOperation(Expression<Func<TDocument, bool>> filter, 
        TDocument? document,
        DisableContext interceptorDisableContext)
    {
        _filter = filter;
        _document = document;
        InterceptorDisableContext = interceptorDisableContext;
    }

    public override Type DocumentType => typeof(TDocument);

    public override object? OldDocument => _document;

    public override object? CurrentDocument => null;

    public override OperationType OperationType => OperationType.Delete;
    
    public override DisableContext InterceptorDisableContext { get; }

    internal override async Task<int> ExecuteAsync(VaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();

        if (context.EnableDiagnostic && _document is null)
        {
            _document = await collection.FindOneAndDeleteAsync(context.Session, _filter, cancellationToken: cancellationToken);

            return _document is not null ? 1 : 0;
        }

        var result = await collection.DeleteOneAsync(context.Session, _filter, cancellationToken: cancellationToken);

        return result.DeletedCount == 1 ? 1 : 0;
    }

    public override bool To(OperationType operationType, out VaultOperation? operation)
    {
        operation = operationType switch
        {
            _ when _document is null => null,
            OperationType.Add => new AddOperation<TDocument>(_document, InterceptorDisableContext),
            OperationType.Update => new ReplaceOperation<TDocument>(_filter, _document, InterceptorDisableContext),
            OperationType.Delete => this,
            _ => null
        };

        return operation is not null;
    }
}
