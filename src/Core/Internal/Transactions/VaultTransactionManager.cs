using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultTransactionManager(IServiceProvider services) : IVaultTransactionManager
{
    // Set and cleared atomically, so of tasks beginning one at once only one does; the rest get the error.
    private VaultTransaction? _active;

    public IVaultTransaction? Current => Active;

    public VaultTransaction? Active => Volatile.Read(ref _active);

    public Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVaultTransaction>(Start());

    public VaultTransaction Start()
    {
        var transaction = new VaultTransaction(this, services.GetService<IMongoClient>());

        return Interlocked.CompareExchange(ref _active, transaction, null) is null
            ? transaction
            : throw new InvalidOperationException("A transaction is already open in this scope.");
    }

    public void End(VaultTransaction transaction) => Interlocked.CompareExchange(ref _active, null, transaction);
}
