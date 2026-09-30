namespace MongoFlow;

/// <summary>What to run for a save when the transaction it joined commits or fails.</summary>
internal sealed record SaveCallbacks(
    Func<CancellationToken, Task> Committed,
    Func<Exception, CancellationToken, Task> Failed);
