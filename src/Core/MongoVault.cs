namespace MongoFlow;

public abstract class MongoVault : IMongoVault
{
    public Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IVaultCollection<TDocument> Collection<TDocument>() =>
        throw new NotImplementedException();

    public IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>() =>
        throw new NotImplementedException();
}
