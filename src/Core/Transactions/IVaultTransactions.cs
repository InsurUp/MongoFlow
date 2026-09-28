namespace MongoFlow;

/// <summary>
/// The request's transaction. Registered as a scoped service by <c>AddMongoVault</c>; not safe to use from parallel
/// work within one scope.
/// </summary>
/// <remarks>
/// While a transaction is open, every vault saved in the scope joins it: its bulk write runs in the transaction's session,
/// and only the transaction's owner commits. A save with no open transaction starts its own and exposes it as
/// <see cref="Current"/> while its interceptors run, so saves they make join it.
/// </remarks>
public interface IVaultTransactions
{
    IVaultTransaction? Current { get; }

    /// <summary>Starts a transaction that every vault saved in this scope joins until it ends.</summary>
    /// <exception cref="InvalidOperationException">A transaction is already open.</exception>
    Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default);
}
