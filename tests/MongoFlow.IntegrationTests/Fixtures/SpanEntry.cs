using System.Diagnostics;

namespace MongoFlow.IntegrationTests;

/// <summary>One span an <see cref="ActivitySink"/> collected, as a snapshot shows it.</summary>
public sealed record SpanEntry(string Name,
    string Parent,
    ActivityStatusCode Status,
    string? StatusDescription,
    IReadOnlyDictionary<string, object?> Tags,
    IReadOnlyList<string> Events);
