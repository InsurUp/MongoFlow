using Microsoft.Extensions.Logging;

namespace MongoFlow.Tests;

/// <summary>One entry a <see cref="LogSink"/> collected.</summary>
public sealed record LogEntry(string Category,
    LogLevel Level,
    int EventId,
    string? Event,
    string Message,
    string? Exception);
