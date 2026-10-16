using Microsoft.Extensions.Logging;

namespace MongoFlow.Encryption;

/// <summary>What <c>MongoFlow.Encryption</c> logs; the events are in <see cref="MongoFlowLogEvents.Encryption"/>.</summary>
internal static partial class EncryptionLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.CollectionCreated, Level = LogLevel.Information,
        Message = "Created {Collection} encrypted, with a data key for each of its encrypted fields: {Fields}.")]
    public static partial void CollectionCreated(this ILogger logger,
        string collection,
        string fields);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.CollectionUpToDate, Level = LogLevel.Debug,
        Message = "{Collection} is encrypted as declared.")]
    public static partial void CollectionUpToDate(this ILogger logger,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.RebuildStarted, Level = LogLevel.Information,
        Message = "Rebuilding {Collection} as an encrypted collection: its documents are copied from {Copy}, encrypted. " +
                  "Going on with an interrupted rebuild: {Resumed}.")]
    public static partial void RebuildStarted(this ILogger logger,
        string collection,
        string copy,
        bool resumed);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.RebuildProgress, Level = LogLevel.Information,
        Message = "Rebuilding {Collection}: {Copied} documents copied, encrypted.")]
    public static partial void RebuildProgress(this ILogger logger,
        string collection,
        long copied);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.RebuildCompleted, Level = LogLevel.Information,
        Message = "Rebuilt {Collection} as an encrypted collection: {Copied} documents copied, encrypted, and {Indexes} " +
                  "indexes.")]
    public static partial void RebuildCompleted(this ILogger logger,
        string collection,
        long copied,
        int indexes);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.IndexSkipped, Level = LogLevel.Warning,
        Message = "The index {Index} of {Collection} wasn't copied into the encrypted collection: it covers {Field}, which is " +
                  "encrypted, and the server can't index encrypted values.")]
    public static partial void IndexSkipped(this ILogger logger,
        string index,
        string collection,
        string field);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.UnencryptedCopyRemains, Level = LogLevel.Warning,
        Message = "{Copy} holds the documents of {Collection} as they were before it was encrypted, unencrypted. MongoFlow " +
                  "never drops it: drop it once the encrypted collection is checked.")]
    public static partial void UnencryptedCopyRemains(this ILogger logger,
        string copy,
        string collection);

    [LoggerMessage(EventId = MongoFlowLogEvents.Encryption.SearchIndexesFollowCopy, Level = LogLevel.Warning,
        Message = "{Collection} had the search indexes {Indexes}, which followed its documents to {Copy}: the search process " +
                  "keeps an index of them in plain text. Drop the indexes, or {Copy}, which drops them too; MongoDB Search " +
                  "can't index the encrypted collection.")]
    public static partial void SearchIndexesFollowCopy(this ILogger logger,
        string collection,
        IEnumerable<string> indexes,
        string copy);
}
