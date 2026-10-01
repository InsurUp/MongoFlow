using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>
/// What <c>MongoFlow.Transaction</c> logs; the events are in <see cref="MongoFlowLogEvents.Transaction"/>. A transaction begun with
/// <see cref="IVaultTransactionManager.BeginAsync"/> logs at <see cref="LogLevel.Debug"/>; one a save opens for itself, at
/// <see cref="LogLevel.Trace"/>, since its save logs already.
/// </summary>
internal static partial class TransactionLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Transaction.Began, Message = "Began a transaction in the scope.")]
    public static partial void Began(this ILogger logger,
        LogLevel level);

    [LoggerMessage(EventId = MongoFlowLogEvents.Transaction.Committed, Message = "Committed the transaction. Saves that joined it: {Saves}.")]
    public static partial void Committed(this ILogger logger,
        LogLevel level,
        int saves);

    [LoggerMessage(EventId = MongoFlowLogEvents.Transaction.CommitFailed, Message = "Committing the transaction failed. Saves that joined it: {Saves}.")]
    public static partial void CommitFailed(this ILogger logger,
        LogLevel level,
        int saves,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Transaction.Doomed,
        Message = "A save that joined the transaction failed after writing, so the transaction was rolled back. Saves " +
                  "that joined it: {Saves}.")]
    public static partial void Doomed(this ILogger logger,
        LogLevel level,
        int saves,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Transaction.RolledBack, Message = "Rolled back the transaction. Saves that joined it: {Saves}.")]
    public static partial void RolledBack(this ILogger logger,
        LogLevel level,
        int saves);
}
