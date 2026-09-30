using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultTransactions(IServiceProvider services) : IVaultTransactions
{
    public IVaultTransaction? Current => Active;

    public VaultTransaction? Active { get; private set; }

    public Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVaultTransaction>(Start());

    public VaultTransaction Start()
    {
        if (Active is not null)
        {
            throw new InvalidOperationException("A transaction is already open in this scope.");
        }

        return Active = new VaultTransaction(this, services.GetService<IMongoClient>());
    }

    public void End(VaultTransaction transaction)
    {
        if (ReferenceEquals(Active, transaction))
        {
            Active = null;
        }
    }
}
