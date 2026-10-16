using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Query</c> logs; the events are in <see cref="MongoFlowLogEvents.Query"/>, at <see cref="LogLevel.Trace"/>.</summary>
internal static partial class QueryLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Query.Reading, Level = LogLevel.Trace,
        Message = "{Method} on {Collection}, with query filters {Filter}.")]
    public static partial void Reading(this ILogger logger,
        string method,
        string collection,
        string filter);

    [LoggerMessage(EventId = MongoFlowLogEvents.Query.ReadingByKey, Level = LogLevel.Trace,
        Message = "GetByKeyAsync on {Collection} by key {Key}, with query filters {Filter}.")]
    public static partial void ReadingByKey(this ILogger logger,
        string collection,
        object key,
        string filter);

    [LoggerMessage(EventId = MongoFlowLogEvents.Query.Searching, Level = LogLevel.Trace,
        Message = "{Method} on {Collection} in {Index}, with query filters {Filter}, filtered in the index by {Clauses} and " +
                  "after the search by {PostFilter}.")]
    public static partial void Searching(this ILogger logger,
        string method,
        string collection,
        string index,
        string filter,
        string clauses,
        string postFilter);
}
