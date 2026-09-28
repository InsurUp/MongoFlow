using System.Text.Json;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Interceptors;

/// <summary>Wakes the dispatcher that publishes stored outbox messages.</summary>
public interface IOutboxSignal
{
    void Notify();
}

/// <summary>
/// Transactional outbox: events raised by saved documents are stored in the same transaction as the change, and the
/// dispatcher is woken only once they're committed.
/// </summary>
public sealed class OutboxInterceptor(IOutboxVault outbox, IOutboxSignal signal, TimeProvider clock) : VaultInterceptor
{
    private static readonly object Stored = new();

    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var events = context.Operations
            .Select(operation => operation.Document)
            .OfType<IRaisesEvents>()
            .SelectMany(document => document.TakeEvents())
            .ToList();

        if (events.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        outbox.Messages.AddRange(events.Select(@event => new OutboxMessage
        {
            Type = @event.GetType().FullName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CreatedAt = now
        }));

        await outbox.SaveAsync(cancellationToken);

        context.Items[Stored] = true;
    }

    public override ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (context.Items.ContainsKey(Stored))
        {
            signal.Notify();
        }

        return ValueTask.CompletedTask;
    }
}
