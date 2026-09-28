using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public sealed class ReplaceOperation<TDocument> : VaultOperation
{
    private readonly Expression<Func<TDocument, bool>> _filter;
    private readonly TDocument _document;
    private TDocument? _oldDocument;

    public ReplaceOperation(Expression<Func<TDocument, bool>> filter, 
        TDocument document,
        DisableContext interceptorDisableContext)
    {
        _filter = filter;
        _document = document;
        InterceptorDisableContext = interceptorDisableContext;
    }

    public override Type DocumentType => typeof(TDocument);

    public override object? CurrentDocument => _document;

    public override object? OldDocument => _oldDocument;

    public override OperationType OperationType => OperationType.Update;
    
    public override DisableContext InterceptorDisableContext { get; }

    internal override async Task<int> ExecuteAsync(VaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();

        if (context.EnableDiagnostic)
        {
            _oldDocument = await collection.FindOneAndReplaceAsync(context.Session, _filter, _document, new FindOneAndReplaceOptions<TDocument>
            {
                ReturnDocument = ReturnDocument.Before
            }, cancellationToken: cancellationToken);

            return 1;
        }

        var replaceResult = await collection.ReplaceOneAsync(context.Session, _filter, _document, cancellationToken: cancellationToken);

        return replaceResult.ModifiedCount == 1 ? 1 : 0;
    }

    public override bool To(OperationType operationType, out VaultOperation operation)
    {
        operation = operationType switch
        {
            OperationType.Add => new AddOperation<TDocument>(_document, InterceptorDisableContext),
            OperationType.Delete => new DeleteOperation<TDocument>(_filter, _document, InterceptorDisableContext),
            _ => this
        };

        return true;
    }
}
