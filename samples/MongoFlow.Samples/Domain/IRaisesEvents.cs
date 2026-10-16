namespace MongoFlow.Samples.Domain;

/// <summary>A document that records domain events for the outbox to store with the change that raised them.</summary>
public interface IRaisesEvents
{
    /// <summary>Whether it holds events not taken yet, which a save runs for even when nothing in it changed.</summary>
    bool HasEvents { get; }

    IReadOnlyList<object> TakeEvents();
}
