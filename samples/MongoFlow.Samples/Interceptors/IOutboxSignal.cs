namespace MongoFlow.Samples.Interceptors;

/// <summary>Wakes the dispatcher that publishes stored outbox messages.</summary>
public interface IOutboxSignal
{
    void Notify();
}
