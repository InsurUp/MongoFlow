namespace MongoFlow.IntegrationTests;

/// <summary>The hooks interceptors ran, in the order they ran.</summary>
public sealed class HookLog
{
    public List<string> Entries { get; } = [];

    public void Add(string entry) => Entries.Add(entry);
}
