using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public class UpdateOperation<TDocument> : VaultOperation
{
    private readonly Expression<Func<TDocument, bool>> _filter;
    private readonly UpdateDefinition<TDocument> _update;
    private TDocument? _currentDocument;
    private TDocument? _oldDocument;

    public UpdateOperation(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        _filter = filter;
        _update = update;
    }

    public override Type DocumentType => typeof(TDocument);
    public override object? CurrentDocument => _currentDocument;

    public override object? OldDocument => _oldDocument;

    public override OperationType OperationType => OperationType.Update;

    internal override async Task<int> ExecuteAsync(VaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var collection = context.Vault.GetCollection<TDocument>();

        if (context.EnableDiagnostic)
        {
            _currentDocument = await collection.Find(context.Session, _filter).FirstOrDefaultAsync(cancellationToken);
            if (_currentDocument is null)
            {
                return 0;
            }

            _oldDocument = await collection.FindOneAndUpdateAsync(context.Session, _filter, _update, new FindOneAndUpdateOptions<TDocument>
            {
                ReturnDocument = ReturnDocument.After
            }, cancellationToken: cancellationToken);

            return 1;
        }

        var result = await collection.UpdateOneAsync(context.Session, _filter, _update, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1 ? 1 : 0;
    }

    public override bool To(OperationType operationType, out VaultOperation? operation)
    {
        operation = operationType switch
        {
            OperationType.Add when _currentDocument is not null => new AddOperation<TDocument>(_currentDocument),
            OperationType.Delete => new DeleteOperation<TDocument>(_filter, _currentDocument),
            OperationType.Update => this,
            _ => null
        };

        return operation is not null;
    }
}
