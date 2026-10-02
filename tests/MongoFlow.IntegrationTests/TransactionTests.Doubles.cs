using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;

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

    /// <summary>
    /// Records each order in the ledger vault from inside the order's save, while it saves or after its write. It can
    /// carry on when the ledger's save fails, as a best-effort audit would, then write a note with the save's session.
    /// </summary>
    public sealed class LedgerSaver : VaultInterceptor
    {
        public bool WhileSaving { get; init; }

        public bool IgnoringFailures { get; init; }

        public bool ThenWritingANote { get; init; }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) =>
            WhileSaving ? SaveLedgerAsync(context, cancellationToken) : ValueTask.CompletedTask;

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) =>
            WhileSaving ? ValueTask.CompletedTask : SaveLedgerAsync(context, cancellationToken);

        private async ValueTask SaveLedgerAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var ledger = context.Services.GetRequiredService<LedgerVault>();
            foreach (var operation in context.Operations)
            {
                if (operation.Document is Order order)
                {
                    ledger.Entries.Add(new LedgerEntry { Id = order.Id, Text = $"order {order.Id}" });
                }
            }

            try
            {
                await ledger.SaveAsync(cancellationToken);
            }
            catch (InvalidOperationException) when (IgnoringFailures)
            {
            }

            if (ThenWritingANote)
            {
                await ((ShopVault)context.Vault).Orders.MongoCollection.Database.GetCollection<BsonDocument>("Notes")
                    .InsertOneAsync(context.Session, new BsonDocument("_id", "written after the ledger failed"),
                        cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>Fails saves while armed: before anything is written, or after the write.</summary>
    public sealed class ArmedFailure : VaultInterceptor
    {
        public bool BeforeWriting { get; set; }

        public bool AfterWriting { get; set; }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) =>
            BeforeWriting ? throw new InvalidOperationException("The save failed before writing.") : ValueTask.CompletedTask;

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) =>
            AfterWriting ? throw new InvalidOperationException("The save failed after writing.") : ValueTask.CompletedTask;
    }
}
