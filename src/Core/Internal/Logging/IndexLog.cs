using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Indexes</c> logs; the events are in <see cref="MongoFlowLogEvents.Indexes"/>.</summary>
internal static partial class IndexLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.KeyIndexMissing, Level = LogLevel.Warning,
        Message = "{Collection} is keyed by {Key}, but no unique index covers the key. Lookups and writes by key scan the " +
                  "collection, and a key can match more than one document.")]
    public static partial void KeyIndexMissing(this ILogger logger,
        string collection,
        IEnumerable<string> key);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.KeyIndexNotUnique, Level = LogLevel.Warning,
        Message = "{Collection} is keyed by {Key}, but its index {Index} on the key isn't unique, so a key can match more " +
                  "than one document.")]
    public static partial void KeyIndexNotUnique(this ILogger logger,
        string collection,
        IEnumerable<string> key,
        string index);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.FieldIndexMissing, Level = LogLevel.Warning,
        Message = "Reads of {Collection} filter on {Field} for {Feature}, but no index includes the field, so they scan the " +
                  "collection.")]
    public static partial void FieldIndexMissing(this ILogger logger,
        string collection,
        string field,
        string feature);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexCheckSkipped, Level = LogLevel.Debug,
        Message = "{Collection} has no indexes, so they weren't checked; it may not exist yet.")]
    public static partial void IndexCheckSkipped(this ILogger logger,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexCheckFailed, Level = LogLevel.Debug,
        Message = "Couldn't check the indexes of {Collection}.")]
    public static partial void IndexCheckFailed(this ILogger logger,
        string collection,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexCheckCompleted, Level = LogLevel.Debug,
        Message = "Checked the indexes of {Vault}.")]
    public static partial void IndexCheckCompleted(this ILogger logger,
        string vault);
}
