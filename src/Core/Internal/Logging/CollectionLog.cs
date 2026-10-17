using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Collections</c> logs; the events are in <see cref="MongoFlowLogEvents.Collections"/>.</summary>
internal static partial class CollectionLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Collections.CollectionCreated, Level = LogLevel.Information,
        Message = "Created {Collection} with {Settings}.")]
    public static partial void CollectionCreated(this ILogger logger,
        string collection,
        string settings);

    [LoggerMessage(EventId = MongoFlowLogEvents.Collections.CollectionUpdated, Level = LogLevel.Information,
        Message = "Changed {Collection} in place: {Changes}.")]
    public static partial void CollectionUpdated(this ILogger logger,
        string collection,
        string changes);

    [LoggerMessage(EventId = MongoFlowLogEvents.Collections.CollectionUpToDate, Level = LogLevel.Debug,
        Message = "{Collection} has the settings it declares.")]
    public static partial void CollectionUpToDate(this ILogger logger,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Collections.CollectionDiffers, Level = LogLevel.Error,
        Message = "{Collection} differs from its declared settings in what MongoDB can't change in place: {Differences}. " +
                  "Rebuild it in a migration: create the collection anew with context.Collection(...).CreateAsync, and " +
                  "copy the documents into it.")]
    public static partial void CollectionDiffers(this ILogger logger,
        string collection,
        string differences);
}
