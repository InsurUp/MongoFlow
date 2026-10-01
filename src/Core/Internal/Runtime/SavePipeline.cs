namespace MongoFlow;

internal static class SavePipeline
{
    public static async Task<SaveResult> RunAsync(VaultRuntime runtime, CancellationToken cancellationToken)
    {
        if (runtime.IsSaving)
        {
            throw new InvalidOperationException($"{runtime.Model.VaultType.Name} can't be saved from its own interceptors.");
        }

        var operations = runtime.Drain();
        if (operations.Count == 0)
        {
            return SaveResult.Empty;
        }

        runtime.IsSaving = true;
        var outer = runtime.Transactions.Active;
        var transaction = outer ?? runtime.Transactions.Start();
        var run = new SaveRun(runtime, operations);
        var callbacks = new SaveCallbacks(run.CommittedAsync, run.FailedAsync);

        try
        {
            await BulkWriteSupport.EnsureAsync(runtime.Model.Database, cancellationToken);

            run.Session = await transaction.JoinAsync(runtime.Model.Client, cancellationToken);
            transaction.Enlist(callbacks);

            await run.SavingAsync(cancellationToken);
            var result = await run.WriteAsync(cancellationToken);
            await run.SavedAsync(cancellationToken);

            if (outer is null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            // Each path runs the interceptors' failure hooks. A rollback after the commit does nothing.
            if (outer is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                transaction.Unenlist(callbacks);
                await run.FailedAsync(exception, CancellationToken.None);
            }

            throw;
        }
        finally
        {
            runtime.IsSaving = false;

            if (outer is null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
