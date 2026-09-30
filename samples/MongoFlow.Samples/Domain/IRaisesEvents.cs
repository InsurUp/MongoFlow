namespace MongoFlow.Samples.Domain;

/// <summary>A document that records domain events for the outbox to store with the change that raised them.</summary>
public interface IRaisesEvents
{
    IReadOnlyList<object> TakeEvents();
}
