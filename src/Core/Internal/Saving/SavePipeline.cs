using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Prest;

namespace MongoFlow;

internal static class SavePipeline
{
    public static async Task<SaveResult> RunAsync(VaultRuntime runtime, CancellationToken cancellationToken)
    {
        // A save is one ordered bulk write on one session, so a second save of the vault instance while one runs is a bug.
        if (!runtime.TryStartSaving())
        {
            throw new InvalidOperationException(
                $"{runtime.Model.VaultType.Name} is already saving. A save can't start from the vault's own interceptors, or " +
                "while another save of the same vault instance runs.");
        }

        try
        {
            return runtime.Drain() is { } operations
                ? await SaveAsync(runtime, operations, cancellationToken)
                : SaveResult.Empty;
        }
        finally
        {
            runtime.EndSaving();
        }
    }

    private static async Task<SaveResult> SaveAsync(VaultRuntime runtime,
        PooledList<VaultOperation> operations,
        CancellationToken cancellationToken)
    {
        var log = runtime.Model.Logs.Save;
        var vault = runtime.Model.VaultType.Name;
        var started = Stopwatch.GetTimestamp();

        var outer = runtime.TransactionManager.Active;
        var transaction = outer ?? runtime.TransactionManager.Start(forSave: true);
        var run = new SaveRun(runtime, operations);
        var callbacks = new SaveCallbacks(run.CommittedAsync, run.FailedAsync, run.Dispose);
        var enlisted = false;

        if (outer is null)
        {
            log.SavingAlone(vault, operations.Count);
        }
        else
        {
            log.SavingInTransaction(vault, operations.Count);
        }

        try
        {
            await BulkWriteSupport.EnsureAsync(runtime.Model.Database, log, cancellationToken);

            run.Session = await transaction.JoinAsync(runtime.Model.Client, cancellationToken);
            transaction.Enlist(callbacks);
            enlisted = true;

            await run.SavingAsync(cancellationToken);
            var result = await run.WriteAsync(cancellationToken);
            await run.SavedAsync(cancellationToken);

            if (outer is null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            log.Saved(vault, Stopwatch.GetElapsedTime(started).TotalMilliseconds, result.Inserted, result.Matched, result.Modified,
                result.Deleted);

            return result;
        }
        catch (Exception exception)
        {
            log.SaveFailed(vault, Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception);

            // Each path runs the interceptors' failure hooks. A rollback after the commit does nothing.
            if (outer is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                transaction.Unenlist(callbacks);
                enlisted = false;
                await run.FailedAsync(exception, CancellationToken.None);
            }

            throw;
        }
        finally
        {
            // An enlisted save is released by its transaction when that ends, after its last hook.
            if (!enlisted)
            {
                run.Dispose();
            }

            if (outer is null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
