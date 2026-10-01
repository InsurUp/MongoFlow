namespace MongoFlow.IntegrationTests;

/// <summary>Logs each hook it runs as <c>name.Hook</c>, so a test can see which ran and in what order.</summary>
public class RecordingInterceptor(string name, HookLog log) : VaultInterceptor
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) => Log("Saving");

    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) => Log("Saved");

    public override ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken) => Log("Committed");

    public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken) =>
        Log("Failed");

    private ValueTask Log(string hook)
    {
        log.Add($"{name}.{hook}");
        return ValueTask.CompletedTask;
    }
}
