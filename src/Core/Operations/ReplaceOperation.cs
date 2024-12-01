using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class ReplaceOperation<TDocument> : VaultOperationBase<TDocument>
{
    private readonly MongoVault _vault;
    private readonly (Expression<Func<TDocument, bool>> Filter, TDocument Document)[] _filterAndDocuments;

    public ReplaceOperation(MongoVault vault,
        (Expression<Func<TDocument, bool>> Filter, TDocument Document)[] filterAndDocuments) 
        : base(vault, filterAndDocuments.Select(x => x.Document).ToArray())
    {
        _vault = vault;
        _filterAndDocuments = filterAndDocuments;
    }

    public ReplaceOperation(MongoVault vault,
        Expression<Func<TDocument, bool>> filter, 
        TDocument document) : base(vault, filter, [document], false)
    {
        _vault = vault;
        _filterAndDocuments = [(filter, document)];
    }

    public override VaultOperationType OperationType => VaultOperationType.Replace;

    public override async Task<int> ExecuteAsync(IClientSessionHandle session, CancellationToken cancellationToken = default)
    {
        if (_filterAndDocuments.Length == 0)
        {
            return 0;
        }
        
        var collection = _vault.GetCollection<TDocument>();
        
        var tasks = _filterAndDocuments.Select(async x =>
        {
            var replaceResult = await collection.ReplaceOneAsync(session, x.Filter, x.Document, cancellationToken: cancellationToken);
            return (int)replaceResult.ModifiedCount;
        });

        var results = await Task.WhenAll(tasks);
        
        return results.Sum();
    }

    public override bool TryConvert(VaultOperationType operationType, out IVaultOperation? operation)
    {
        operation = operationType switch
        {
            VaultOperationType.Replace 
                => this,
            VaultOperationType.Add 
                => new AddOperation<TDocument>(_vault, _filterAndDocuments.Select(x => x.Document).ToArray()),
            VaultOperationType.Delete when _filterAndDocuments.Length == 1
                => new DeleteOperation<TDocument>(_vault, _filterAndDocuments[0].Filter, [_filterAndDocuments[0].Document], false),
            _ => null
        };

        return operation is not null;
    }
}
