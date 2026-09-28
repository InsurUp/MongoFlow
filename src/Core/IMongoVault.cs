namespace MongoFlow;

/// <summary>
/// What every vault can do, for app interfaces to extend: <c>interface IPolicyVault : IMongoVault</c>.
/// </summary>
public interface IMongoVault
{
    /// <summary>
    /// Writes every queued operation, in order, as one client bulk write inside a transaction. Nothing is written if
    /// any operation fails. Queued operations are discarded either way.
    /// </summary>
    Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default);
}
