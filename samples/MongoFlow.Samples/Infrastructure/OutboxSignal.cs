using Microsoft.Extensions.Logging;
using MongoFlow.Samples.Interceptors;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>A real app would release a background dispatcher waiting on this; the samples log it.</summary>
public sealed class OutboxSignal(ILogger<OutboxSignal> logger) : IOutboxSignal
{
    public void Notify() => logger.LogInformation("The outbox has committed messages to publish");
}
