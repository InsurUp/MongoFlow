using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class DeleteOperation<TDocument> : VaultOperationBase<TDocument>
{
    private readonly MongoVault _vault;
    private readonly Expression<Func<TDocument, bool>> _filter;
    private readonly bool _multiple;

    public DeleteOperation(MongoVault vault,
        Expression<Func<TDocument, bool>> filter, 
        TDocument[] documents,
        bool multiple) : base(vault, filter, documents, multiple)
    {
        _vault = vault;
        _filter = filter;
        _multiple = multiple;
    }

    public override VaultOperationType OperationType => VaultOperationType.Delete;

    public override async Task<int> ExecuteAsync(IClientSessionHandle session, CancellationToken cancellationToken = default)
    {
        var collection = _vault.GetCollection<TDocument>();
        

        var result = _multiple ? 
            await collection.DeleteManyAsync(session, _filter, cancellationToken: cancellationToken)
            : await collection.DeleteOneAsync(session, _filter, cancellationToken: cancellationToken);

        return (int) result.DeletedCount;
    }

    public override bool TryConvert(VaultOperationType operationType, out IVaultOperation? operation)
    {
        operation = operationType switch
        {
            VaultOperationType.Delete => this,
            VaultOperationType.Replace when CachedDocuments.Length > 1 => ConvertToReplace(),
            VaultOperationType.Add when CachedDocuments.Length > 0 
                => new AddOperation<TDocument>(_vault, CachedDocuments.OfType<TDocument>().ToArray()),
            _ => null
        };

        return operation is not null;
    }
    
    private IVaultOperation ConvertToReplace()
    {
        if (CachedDocuments.Length == 1)
        {
            return new ReplaceOperation<TDocument>(_vault, _filter, (TDocument) CachedDocuments[0]);
        }
        
        var documentSetConfiguration = _vault.Configuration.GetDocumentSetConfiguration<TDocument>();
        var keyExpression = documentSetConfiguration.GetKeyExpression<TDocument>();

        var filtersAndDocuments = CachedDocuments
            .OfType<TDocument>()
            .Select(x => new
            {
                Document = x, 
                Key = keyExpression.Compile().Invoke(x)
            })
            .Select(x => new
            {
                x.Document, 
                KeyFilter = documentSetConfiguration.BuildKeyFilterExpression<TDocument>(x.Key)
            })
            .Select(x => (new LambdaExpression[] {x.KeyFilter, _filter}.CombineAnd<TDocument>(), x.Document))
            .ToArray();
        
        return new ReplaceOperation<TDocument>(_vault, filtersAndDocuments);
    }
}
