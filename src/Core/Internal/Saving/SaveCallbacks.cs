namespace MongoFlow;

/// <summary>What to run for a save when the transaction it joined commits or fails, and when it ends.</summary>
internal sealed record SaveCallbacks(
    Func<CancellationToken, Task> Committed,
    Func<Exception, CancellationToken, Task> Failed,
    Action Release);
