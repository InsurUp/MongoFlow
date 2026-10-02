using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultTransactionManager(IServiceProvider services) : IVaultTransactionManager
{
    // Set and cleared atomically, so of tasks beginning one at once only one does; the rest get the error.
    private VaultTransaction? _active;

    public IVaultTransaction? Current => Active;

    public ILogger Log => field ??= VaultLogs.Transactions(services);

    public VaultMetrics Metrics => field ??= services.GetRequiredService<VaultMetrics>();

    public VaultTransaction? Active => Volatile.Read(ref _active);

    public Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVaultTransaction>(Start(forSave: false));

    /// <param name="forSave">
    /// Whether a save opens it for itself, which logs it at a lower level than a begun one and leaves its duration to the
    /// save's.
    /// </param>
    public VaultTransaction Start(bool forSave)
    {
        var transaction = new VaultTransaction(this, services.GetService<IMongoClient>(), forSave);

        if (Interlocked.CompareExchange(ref _active, transaction, null) is not null)
        {
            throw new InvalidOperationException("A transaction is already open in this scope.");
        }

        Log.Began(forSave ? LogLevel.Trace : LogLevel.Debug);
        return transaction;
    }

    public void End(VaultTransaction transaction) => Interlocked.CompareExchange(ref _active, null, transaction);
}
