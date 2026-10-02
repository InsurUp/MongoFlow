using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MongoFlow;

/// <summary>
/// MongoFlow's instruments, one set per root provider: on a meter from its <see cref="IMeterFactory"/>, which keeps each
/// provider's measurements apart, or on a meter of its own without one, disposed with the provider.
/// </summary>
/// <remarks>Each recording checks the instrument is listened to before it builds its tags, so without a listener it costs a branch.</remarks>
internal sealed class VaultMetrics : IDisposable
{
    // ASP.NET Core's request duration boundaries, in seconds: a save is a request's worth of work.
    private static readonly InstrumentAdvice<double> Durations = new()
    {
        HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
    };

    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    public VaultMetrics(IMeterFactory? factory)
    {
        _meter = factory?.Create(MongoFlowTelemetry.MeterName, VaultActivities.Version)
                 ?? new Meter(MongoFlowTelemetry.MeterName, VaultActivities.Version);
        _ownsMeter = factory is null;

        SaveDuration = _meter.CreateHistogram(MongoFlowTelemetry.Instruments.SaveDuration, "s",
            "How long saves that have writes queued take.", advice: Durations);
        SaveOperations = _meter.CreateCounter<long>(MongoFlowTelemetry.Instruments.SaveOperations, "{operation}",
            "The writes saves committed.");
        TransactionDuration = _meter.CreateHistogram(MongoFlowTelemetry.Instruments.TransactionDuration, "s",
            "How long transactions begun in a scope stay open.", advice: Durations);
    }

    public Histogram<double> SaveDuration { get; }

    public Counter<long> SaveOperations { get; }

    public Histogram<double> TransactionDuration { get; }

    public void RecordSave(string vault,
        bool joined,
        TimeSpan elapsed,
        Exception? exception)
    {
        if (!SaveDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { MongoFlowTelemetry.Tags.Vault, vault },
            { MongoFlowTelemetry.Tags.SaveTransaction, joined ? "joined" : "own" }
        };

        if (exception is not null)
        {
            tags.Add(MongoFlowTelemetry.Tags.ErrorType, exception.GetType().FullName);
        }

        SaveDuration.Record(elapsed.TotalSeconds, tags);
    }

    /// <summary>Counts a save's writes by kind, once its transaction committed.</summary>
    public void RecordCommitted(string vault,
        ReadOnlySpan<VaultOperation> operations)
    {
        if (!SaveOperations.Enabled)
        {
            return;
        }

        Span<long> counts = stackalloc long[4];
        foreach (var operation in operations)
        {
            counts[(int)operation.Kind]++;
        }

        CountOperations(vault, "insert", counts[(int)OperationKind.Insert]);
        CountOperations(vault, "replace", counts[(int)OperationKind.Replace]);
        CountOperations(vault, "update", counts[(int)OperationKind.Update]);
        CountOperations(vault, "delete", counts[(int)OperationKind.Delete]);
    }

    /// <param name="outcome"><c>committed</c>, <c>rolled_back</c> or <c>commit_failed</c>.</param>
    public void RecordTransaction(TimeSpan elapsed,
        string outcome,
        Exception? exception)
    {
        if (!TransactionDuration.Enabled)
        {
            return;
        }

        var tags = new TagList { { MongoFlowTelemetry.Tags.TransactionOutcome, outcome } };

        if (exception is not null)
        {
            tags.Add(MongoFlowTelemetry.Tags.ErrorType, exception.GetType().FullName);
        }

        TransactionDuration.Record(elapsed.TotalSeconds, tags);
    }

    public void Dispose()
    {
        // A factory's meters are the factory's to dispose.
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private void CountOperations(string vault,
        string kind,
        long count)
    {
        if (count > 0)
        {
            SaveOperations.Add(count,
                new KeyValuePair<string, object?>(MongoFlowTelemetry.Tags.Vault, vault),
                new KeyValuePair<string, object?>(MongoFlowTelemetry.Tags.OperationKind, kind));
        }
    }
}
