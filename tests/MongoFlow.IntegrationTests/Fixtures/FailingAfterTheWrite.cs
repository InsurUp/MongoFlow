namespace MongoFlow.IntegrationTests;

/// <summary>Fails every save after its write, so what the save did has to be undone.</summary>
public sealed class FailingAfterTheWrite : VaultInterceptor
{
    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The save failed after its write.");
}
