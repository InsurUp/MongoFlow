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

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexCreated, EventName = nameof(MongoFlowLogEvents.Indexes.IndexCreated),
        Level = LogLevel.Information, Message = "Created the index {Index} of {Collection}: {Definition}.")]
    public static partial void IndexCreatedAsDeclared(this ILogger logger,
        string index,
        string collection,
        string definition);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexUpdated, EventName = nameof(MongoFlowLogEvents.Indexes.IndexUpdated),
        Level = LogLevel.Information, Message = "Changed the index {Index} of {Collection} in place: {Changes}.")]
    public static partial void IndexChangedInPlace(this ILogger logger,
        string index,
        string collection,
        string changes);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexUpToDate, Level = LogLevel.Debug,
        Message = "The index {Index} of {Collection} is defined as declared.")]
    public static partial void IndexUpToDate(this ILogger logger,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexDiffers, Level = LogLevel.Error,
        Message = "The index {Index} of {Collection} differs from its declaration in what MongoDB can't change in place: " +
                  "{Differences}. Recreate it in a migration, with context.Collection(...).RecreateIndexAsync.")]
    public static partial void IndexDiffers(this ILogger logger,
        string index,
        string collection,
        string differences);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexRecreated, Level = LogLevel.Information,
        Message = "Recreated the index {Index} of {Collection}, dropping {Dropped}: {Definition}.")]
    public static partial void IndexRecreated(this ILogger logger,
        string index,
        string collection,
        string dropped,
        string definition);

    [LoggerMessage(EventId = MongoFlowLogEvents.Indexes.IndexNotDeclared, Level = LogLevel.Warning,
        Message = "A migration named the index {Index} of {Collection}, which the collection doesn't declare; the migrator " +
                  "sets up the indexes it declares after the migrations.")]
    public static partial void IndexNotDeclared(this ILogger logger,
        string index,
        string collection);
}
