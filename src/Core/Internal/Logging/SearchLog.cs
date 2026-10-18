using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Search</c> logs; the events are in <see cref="MongoFlowLogEvents.Search"/>.</summary>
internal static partial class SearchLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexCreated, Level = LogLevel.Information,
        Message = "Created the {Type} index {Index} of {Collection}.")]
    public static partial void IndexCreated(this ILogger logger,
        string type,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexUpdated, Level = LogLevel.Information,
        Message = "Updated the {Type} index {Index} of {Collection}, whose definition changed; it's rebuilt, and searched " +
                  "as it was meanwhile.")]
    public static partial void IndexUpdated(this ILogger logger,
        string type,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexUpToDate, Level = LogLevel.Debug,
        Message = "The {Type} index {Index} of {Collection} is defined as declared.")]
    public static partial void IndexUpToDate(this ILogger logger,
        string type,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexBuilding, Level = LogLevel.Debug,
        Message = "Waiting for the {Type} index {Index} of {Collection} to be built: {Status}.")]
    public static partial void IndexBuilding(this ILogger logger,
        string type,
        string index,
        string collection,
        string status);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexQueryable, Level = LogLevel.Information,
        Message = "The {Type} index {Index} of {Collection} is built and queryable.")]
    public static partial void IndexQueryable(this ILogger logger,
        string type,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexFailed, Level = LogLevel.Error,
        Message = "The {Type} index {Index} of {Collection} failed to build: {Message}")]
    public static partial void IndexFailed(this ILogger logger,
        string type,
        string index,
        string collection,
        string message);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.FilterFieldNotPreFiltered, Level = LogLevel.Warning,
        Message = "Searches of {Collection} in {Index} match the query filters on {Field} after the search: the index " +
                  "doesn't map it to compare exactly, such as a string as SearchField.Token(), or the filters compare it " +
                  "in a way a search can't, such as with a regular expression.")]
    public static partial void FilterFieldNotPreFiltered(this ILogger logger,
        string collection,
        string index,
        string field);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexListed, Level = LogLevel.Debug,
        Message = "Listed the {Type} index {Index} of {Collection}, which the vault doesn't declare, to learn what it maps.")]
    public static partial void IndexListed(this ILogger logger,
        string type,
        string index,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Search.IndexDropped, Level = LogLevel.Information,
        Message = "Dropped the {Type} index {Index} of {Collection}, {Reason}. It was {Definition}.")]
    public static partial void IndexDropped(this ILogger logger,
        string type,
        string index,
        string collection,
        string reason,
        string definition);
}
