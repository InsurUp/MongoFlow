using MongoFlow.Samples.Interceptors;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>Placeholder; a real app would release a background dispatcher waiting on this.</summary>
public sealed class OutboxSignal : IOutboxSignal
{
    public void Notify()
    {
    }
}
