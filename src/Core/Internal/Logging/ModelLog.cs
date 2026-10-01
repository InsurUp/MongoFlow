using Microsoft.Extensions.Logging;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Model</c> logs; the events are in <see cref="MongoFlowLogEvents.Model"/>.</summary>
internal static partial class ModelLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Model.ModelBuilt, Level = LogLevel.Debug,
        Message = "Built the model of {Vault} on database {Database}. Collections: {Collections}. Interceptors: " +
                  "{Interceptors}.")]
    public static partial void ModelBuilt(this ILogger logger,
        string vault,
        string database,
        IEnumerable<string> collections,
        int interceptors);
}
