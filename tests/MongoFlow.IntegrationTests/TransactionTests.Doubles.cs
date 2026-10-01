using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.IntegrationTests;

public partial class TransactionTests
{
    /// <summary>Looks at the transaction a save runs in, while its interceptors run.</summary>
    public sealed class TransactionProbe : VaultInterceptor
    {
        public bool CurrentWhileSaving { get; private set; }

        public bool SessionInTransaction { get; private set; }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            CurrentWhileSaving = context.Services.GetRequiredService<IVaultTransactionManager>().Current is not null;
            SessionInTransaction = context.Session.IsInTransaction;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records each saved order in the ledger vault, saving it from inside the order's save.</summary>
    public sealed class LedgerWriter(LedgerVault ledger) : VaultInterceptor
    {
        public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                if (operation.Document is Order order)
                {
                    ledger.Entries.Add(new LedgerEntry { Id = order.Id, Text = $"order {order.Id}" });
                }
            }

            await ledger.SaveAsync(cancellationToken);
        }
    }
}
