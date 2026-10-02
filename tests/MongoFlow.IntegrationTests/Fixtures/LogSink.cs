using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A logger provider that keeps every entry logged at any level, for tests to read: register it with
/// <c>services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(sink))</c>.
/// </summary>
public sealed partial class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    // Signalled when an entry with the event ID is logged, for WaitForAsync.
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _logged = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>
    /// What MongoFlow logged under <paramref name="category"/>, or under all its categories, as a snapshot shows it: test
    /// database names and timings, which change from run to run, are scrubbed.
    /// </summary>
    public IReadOnlyList<LogEntry> Snapshot(string? category = null) =>
    [
        .. Entries
            .Where(entry => entry.Category.StartsWith(category ?? "MongoFlow", StringComparison.Ordinal))
            .Select(entry => entry with { Message = Scrub(entry.Message) })
    ];

    /// <summary>Waits until an entry with <paramref name="eventId"/> is logged, such as one a background task logs last.</summary>
    public Task WaitForAsync(int eventId)
    {
        var logged = _logged.GetOrAdd(eventId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        // Logged before the wait began, it wasn't signalled.
        if (_entries.Any(entry => entry.EventId == eventId))
        {
            logged.TrySetResult();
        }

        return logged.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void Dispose()
    {
    }

    private static string Scrub(string message) =>
        Milliseconds().Replace(Database().Replace(message, "{database}"), "{elapsed} ms");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();

    [GeneratedRegex(@"\d+(\.\d+)? ms")]
    private static partial Regex Milliseconds();

    private void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);

        if (_logged.TryGetValue(entry.EventId, out var logged))
        {
            logged.TrySetResult();
        }
    }

    private sealed class Logger(string category, LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add(new LogEntry(category, logLevel, eventId.Id, eventId.Name, formatter(state, exception), exception?.GetType().Name));
    }
}
