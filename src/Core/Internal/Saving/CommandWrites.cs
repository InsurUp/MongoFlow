using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>
/// Writes a save's operations with collection commands, in queue order, in the save's session: how a client that encrypts
/// saves, since the driver refuses it client bulk writes, and the server refuses a bulk write across collections, or with
/// more than one update or delete, that encrypts. Inserts into one collection that follow each other go in one command.
/// </summary>
internal static class CommandWrites
{
    public static async Task<SaveResult> WriteAsync(SaveRun run,
        CancellationToken cancellationToken)
    {
        var operations = run.Operations;
        var log = run.Runtime.Model.Logs.Save;
        var tracing = log.IsEnabled(LogLevel.Trace);
        var results = new OperationResult[operations.Count];

        for (var i = 0; i < operations.Count;)
        {
            var written = await operations[i].WriteAsync(run, i, results, cancellationToken);

            if (tracing)
            {
                for (var j = i; j < i + written; j++)
                {
                    log.Writing(operations[j].Kind, operations[j].Namespace.CollectionName, operations[j].Key, operations[j].IsSetBased);
                }
            }

            i += written;
        }

        // Set once every write succeeded, as a client bulk write's are.
        long inserted = 0, matched = 0, modified = 0, deleted = 0;
        for (var i = 0; i < operations.Count; i++)
        {
            var result = results[i];
            operations[i].Result = result;

            inserted += result.Inserted;
            matched += result.Matched;
            modified += result.Modified;
            deleted += result.Deleted;
        }

        return new SaveResult(inserted, matched, modified, deleted);
    }
}
