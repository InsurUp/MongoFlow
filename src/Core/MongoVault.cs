namespace MongoFlow;

public abstract class MongoVault : IMongoVault
{
    public Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
