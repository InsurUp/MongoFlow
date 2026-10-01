using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Save</c> logs; the events are in <see cref="MongoFlowLogEvents.Save"/>.</summary>
internal static partial class SaveLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Save.SavingAlone, Level = LogLevel.Debug,
        Message = "Saving {Vault} in a transaction of its own. Operations queued: {Operations}.")]
    public static partial void SavingAlone(this ILogger logger,
        string vault,
        int operations);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.SavingInTransaction, Level = LogLevel.Debug,
        Message = "Saving {Vault} in the scope's open transaction. Operations queued: {Operations}.")]
    public static partial void SavingInTransaction(this ILogger logger,
        string vault,
        int operations);

    // Logged once per operation, so the caller checks the level once per save.
    [LoggerMessage(EventId = MongoFlowLogEvents.Save.Writing, Level = LogLevel.Trace, SkipEnabledCheck = true,
        Message = "Writing {Kind} to {Collection}: key {Key}, set-based {SetBased}.")]
    public static partial void Writing(this ILogger logger,
        OperationKind kind,
        string collection,
        object? key,
        bool setBased);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.Saved, Level = LogLevel.Debug,
        Message = "Saved {Vault} in {ElapsedMilliseconds:0.0} ms: {Inserted} inserted, {Matched} matched, {Modified} " +
                  "modified, {Deleted} deleted.")]
    public static partial void Saved(this ILogger logger,
        string vault,
        double elapsedMilliseconds,
        long inserted,
        long matched,
        long modified,
        long deleted);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.SaveFailed, Level = LogLevel.Debug,
        Message = "Saving {Vault} failed after {ElapsedMilliseconds:0.0} ms.")]
    public static partial void SaveFailed(this ILogger logger,
        string vault,
        double elapsedMilliseconds,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.FailureHookThrew, Level = LogLevel.Error,
        Message = "{Interceptor}.FailedAsync threw while a save of {Vault} failed. Its exception is ignored, so the save's own " +
                  "failure surfaces.")]
    public static partial void FailureHookThrew(this ILogger logger,
        string interceptor,
        string vault,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.ConcurrencyConflict, Level = LogLevel.Debug,
        Message = "A write to {Collection} by key {Key} matched no document with the token it was read with; still " +
                  "stored: {DocumentExists}. The save fails with a concurrency conflict.")]
    public static partial void ConcurrencyConflict(this ILogger logger,
        string collection,
        object? key,
        bool documentExists);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.TenantRejected, Level = LogLevel.Debug,
        Message = "A write to {Collection} carries tenant {Tenant} while the current tenant is {Current}. The save fails.")]
    public static partial void TenantRejected(this ILogger logger,
        string collection,
        object? tenant,
        object? current);

    [LoggerMessage(EventId = MongoFlowLogEvents.Save.BulkWritesSupported, Level = LogLevel.Debug,
        Message = "The server supports client bulk writes, which saves use: wire version {WireVersion}.")]
    public static partial void BulkWritesSupported(this ILogger logger,
        int wireVersion);
}
