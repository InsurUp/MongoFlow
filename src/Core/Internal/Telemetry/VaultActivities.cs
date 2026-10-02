using System.Diagnostics;
using System.Reflection;
using Semver;

namespace MongoFlow;

/// <summary>
/// MongoFlow's spans, under <see cref="MongoFlowTelemetry.ActivitySourceName"/>. Without a listener nothing starts, and
/// tags are set only on spans that record them.
/// </summary>
internal static class VaultActivities
{
    /// <summary>The package's version, without build metadata, for the activity source and the meter.</summary>
    public static readonly string? Version = typeof(VaultActivities).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

    private static readonly ActivitySource Source = new(MongoFlowTelemetry.ActivitySourceName, Version);

    public static Activity? StartSave(string vault,
        bool joined)
    {
        var activity = Source.StartActivity(MongoFlowTelemetry.Activities.Save);
        if (activity.Recording() is { } recording)
        {
            recording.SetTag(MongoFlowTelemetry.Tags.Vault, vault);
            recording.SetTag(MongoFlowTelemetry.Tags.SaveTransaction, joined ? "joined" : "own");
        }

        return activity;
    }

    public static void Writing(this Activity? activity,
        int operations) =>
        activity.Recording()?.SetTag(MongoFlowTelemetry.Tags.OperationCount, operations);

    public static void Saved(this Activity? activity,
        SaveResult result)
    {
        if (activity.Recording() is { } recording)
        {
            recording.SetTag(MongoFlowTelemetry.Tags.InsertedCount, result.Inserted);
            recording.SetTag(MongoFlowTelemetry.Tags.MatchedCount, result.Matched);
            recording.SetTag(MongoFlowTelemetry.Tags.ModifiedCount, result.Modified);
            recording.SetTag(MongoFlowTelemetry.Tags.DeletedCount, result.Deleted);
        }
    }

    public static Activity? StartMigrate(string vault)
    {
        var activity = Source.StartActivity(MongoFlowTelemetry.Activities.Migrate);
        activity.Recording()?.SetTag(MongoFlowTelemetry.Tags.Vault, vault);

        return activity;
    }

    public static void Migrating(this Activity? activity,
        string current,
        SemVersion target)
    {
        if (activity.Recording() is { } recording)
        {
            recording.SetTag(MongoFlowTelemetry.Tags.MigrationCurrent, current);
            recording.SetTag(MongoFlowTelemetry.Tags.MigrationTarget, target.ToString());
        }
    }

    public static Activity? StartMigration(string vault,
        SemVersion version,
        string migration,
        bool up)
    {
        var activity = Source.StartActivity(MongoFlowTelemetry.Activities.Migration);
        if (activity.Recording() is { } recording)
        {
            recording.SetTag(MongoFlowTelemetry.Tags.Vault, vault);
            recording.SetTag(MongoFlowTelemetry.Tags.MigrationVersion, version.ToString());
            recording.SetTag(MongoFlowTelemetry.Tags.MigrationName, migration);
            recording.SetTag(MongoFlowTelemetry.Tags.MigrationDirection, up ? "up" : "down");
        }

        return activity;
    }

    /// <summary>Marks the span failed, the way OpenTelemetry records an error: its status, type and an exception event.</summary>
    public static void Fail(this Activity? activity,
        Exception exception)
    {
        if (activity.Recording() is { } recording)
        {
            recording.SetStatus(ActivityStatusCode.Error, exception.Message);
            recording.SetTag(MongoFlowTelemetry.Tags.ErrorType, exception.GetType().FullName);
            recording.AddException(exception);
        }
    }

    /// <summary>
    /// The span, if it records what it's given; a span sampled only to carry the trace on, such as under a parent a
    /// parent-based sampler didn't sample, is given nothing.
    /// </summary>
    private static Activity? Recording(this Activity? activity) => activity is { IsAllDataRequested: true } ? activity : null;
}
