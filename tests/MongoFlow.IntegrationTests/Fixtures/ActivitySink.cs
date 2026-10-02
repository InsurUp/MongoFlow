using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// Keeps the spans MongoFlow and the driver start under a root span of its own, for a test to read. Create it in the test
/// body: the root is the current activity only in the flow that creates it. It starts a trace of its own, and only spans
/// in that trace are kept, so tests running in parallel don't see each other's.
/// </summary>
public sealed partial class ActivitySink : IDisposable
{
    private static readonly ActivitySource Tests = new("MongoFlow.IntegrationTests");

    private readonly ConcurrentQueue<Activity> _started = new();
    private readonly ActivitySamplingResult _mongoFlow;
    private readonly ActivityListener _listener;
    private readonly Activity? _previous;

    // Null while the root itself starts, so it isn't kept.
    private readonly Activity? _root;

    /// <param name="mongoFlow">
    /// How MongoFlow's spans are sampled: recorded, or only created to carry the trace on, as a parent-based sampler does
    /// under a parent it didn't sample.
    /// </param>
    public ActivitySink(ActivitySamplingResult mongoFlow = ActivitySamplingResult.AllDataAndRecorded)
    {
        _mongoFlow = mongoFlow;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == Tests || source.Name is MongoFlowTelemetry.ActivitySourceName or MongoTelemetry.ActivitySourceName,
            Sample = Sample,
            ActivityStarted = activity =>
            {
                if (activity.TraceId == _root?.TraceId)
                {
                    _started.Enqueue(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(_listener);

        _previous = Activity.Current;
        Activity.Current = null;
        _root = Tests.StartActivity("test");
    }

    /// <summary>
    /// MongoFlow's spans in the order they started, and the driver's named <paramref name="driverSpans"/>, without tags.
    /// Test database names are scrubbed.
    /// </summary>
    public IReadOnlyList<SpanEntry> Snapshot(params string[] driverSpans) =>
    [
        .. _started
            .Where(activity => activity.Source.Name == MongoFlowTelemetry.ActivitySourceName ||
                               driverSpans.Contains(Scrub(activity.DisplayName)))
            .Select(activity => new SpanEntry(Scrub(activity.DisplayName),
                Scrub(activity.Parent?.DisplayName ?? "none"),
                activity.Status,
                activity.StatusDescription is { } description ? Scrub(description) : null,
                activity.Source.Name == MongoFlowTelemetry.ActivitySourceName
                    ? activity.TagObjects.OrderBy(tag => tag.Key, StringComparer.Ordinal).ToDictionary()
                    : new Dictionary<string, object?>(),
                [.. activity.Events.Select(@event => @event.Name)]))
    ];

    public void Dispose()
    {
        _root?.Stop();
        Activity.Current = _previous;
        _listener.Dispose();
    }

    private ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        if (options.Source == Tests)
        {
            return ActivitySamplingResult.AllDataAndRecorded;
        }

        if (options.TraceId != _root?.TraceId)
        {
            return ActivitySamplingResult.None;
        }

        return options.Source.Name == MongoFlowTelemetry.ActivitySourceName ? _mongoFlow : ActivitySamplingResult.AllDataAndRecorded;
    }

    private static string Scrub(string text) => Database().Replace(text, "{database}");

    [GeneratedRegex(@"\bt[0-9a-f]{32}\b")]
    private static partial Regex Database();
}
