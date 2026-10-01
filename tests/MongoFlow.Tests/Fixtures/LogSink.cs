using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MongoFlow.Tests;

/// <summary>
/// A logger provider that keeps every entry logged at any level, for tests to read: register it with
/// <c>services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(sink))</c>.
/// </summary>
public sealed partial class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

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
    public async Task WaitForAsync(int eventId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!_entries.Any(entry => entry.EventId == eventId))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private static string Scrub(string message) =>
        Milliseconds().Replace(Database().Replace(message, "{database}"), "{elapsed} ms");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();

    [GeneratedRegex(@"\d+(\.\d+)? ms")]
    private static partial Regex Milliseconds();

    private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, logLevel, eventId.Id, eventId.Name, formatter(state, exception), exception?.GetType().Name));
    }
}
