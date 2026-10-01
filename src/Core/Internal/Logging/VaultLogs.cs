using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MongoFlow;

/// <summary>
/// A vault model's loggers, from the root provider's <see cref="ILoggerFactory"/>, or loggers that log nothing without
/// one. The categories share the <c>MongoFlow</c> prefix, so one setting covers them all.
/// </summary>
internal sealed class VaultLogs(ILoggerFactory factory)
{
    /// <summary>Building a vault's model.</summary>
    public ILogger Model { get; } = factory.CreateLogger(MongoFlowLogEvents.Model.Category);

    /// <summary>Collections missing the indexes their key and features rely on.</summary>
    public ILogger Indexes { get; } = factory.CreateLogger(MongoFlowLogEvents.Indexes.Category);

    /// <summary>Saves: what they write, how they end, and what fails them.</summary>
    public ILogger Save { get; } = factory.CreateLogger(MongoFlowLogEvents.Save.Category);

    /// <summary>Reads and the query filters they run with.</summary>
    public ILogger Query { get; } = factory.CreateLogger(MongoFlowLogEvents.Query.Category);

    public static VaultLogs From(IServiceProvider services) =>
        new(services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance);

    /// <summary>The scope's transactions, logged by the scope's transaction manager.</summary>
    public static ILogger Transactions(IServiceProvider services) =>
        (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance)
        .CreateLogger(MongoFlowLogEvents.Transaction.Category);
}
