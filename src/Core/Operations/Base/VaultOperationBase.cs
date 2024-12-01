using System.Linq.Expressions;

namespace MongoFlow;

public abstract class VaultOperationBase<TDocument> : IVaultOperation
{
    private readonly MongoVault _vault;
    private readonly Expression<Func<TDocument, bool>>? _filter;
    private readonly bool _multiple;

    protected VaultOperationBase(MongoVault vault,
        Expression<Func<TDocument, bool>> filter,
        TDocument[] documents,
        bool multiple)
    {
        _vault = vault;
        _filter = filter;
        _multiple = multiple;
        CachedDocuments = documents.Cast<object>().ToArray();
    }
    
    protected VaultOperationBase(MongoVault vault,
        TDocument[] documents)
    {
        _vault = vault;
        CachedDocuments = documents.Cast<object>().ToArray();
    }

    public Type DocumentType => typeof(TDocument);
    
    public abstract VaultOperationType OperationType { get; }
    
    public object[] CachedDocuments { get; private set; }

    public async ValueTask<object[]> FetchDocumentsAsync(IClientSessionHandle session, CancellationToken cancellationToken = default)
    {
        if (CachedDocuments.Length > 0 || _filter is null)
        {
            return CachedDocuments;
        }
        
        var collection = _vault.GetCollection<TDocument>();

        var limit = _multiple ? 0 : 1;
        
        var documents = await collection.Find(session, _filter).Limit(limit).ToListAsync(cancellationToken);
        CachedDocuments = documents.Cast<object>().ToArray();
        
        return CachedDocuments;
    }

    public abstract Task<int> ExecuteAsync(IClientSessionHandle session, CancellationToken cancellationToken = default);

    public abstract bool TryConvert(VaultOperationType operationType, out IVaultOperation? operation);
}