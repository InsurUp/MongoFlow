namespace MongoFlow;

/// <summary>
/// The request's transaction. Registered as a scoped service by <c>AddMongoVault</c>. Of tasks beginning one at once,
/// one does and the rest throw. A transaction's session runs one operation at a time, so the reads and saves that join it
/// run one after another, not in parallel.
/// </summary>
/// <remarks>
/// While a transaction is open, every vault saved in the scope joins it: its bulk write runs in the transaction's session,
/// and only the transaction's owner commits. A save with no open transaction starts its own and exposes it as
/// <see cref="Current"/> while its interceptors run, so saves they make join it.
/// </remarks>
public interface IVaultTransactionManager
{
    IVaultTransaction? Current { get; }

    /// <summary>Starts a transaction that every vault saved in this scope joins until it ends.</summary>
    /// <exception cref="InvalidOperationException">A transaction is already open.</exception>
    Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default);
}
