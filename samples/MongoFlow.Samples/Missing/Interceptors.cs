#if MISSING_API
// Interceptors, the other half of features. Soft delete and multi-tenancy need the same hooks for their write side.

using MongoDB.Bson;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Missing;

public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }
}

public interface IRaisesEvents
{
    IReadOnlyList<object> TakeEvents();
}

// Changes documents before they're written.
public sealed class TimestampInterceptor(TimeProvider clock) : VaultInterceptor
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var document in context.Inserted<ITimestamped>())
        {
            document.CreatedAt = now;
        }

        foreach (var document in context.Updated<ITimestamped>())
        {
            document.UpdatedAt = now;
        }

        return ValueTask.CompletedTask;
    }
}

// Writes to another vault inside the same transaction, and needs each document as it was before the change.
public sealed class AuditTrailInterceptor(IAuditVault audit, ICurrentUser user) : VaultInterceptor
{
    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var change in context.Changes)
        {
            audit.Entries.Add(new AuditLogEntry
            {
                At = DateTime.UtcNow,
                Collection = change.Collection.Name,
                Action = change.Kind.ToString(),
                UserId = user.UserId,
                Before = change.Original?.ToBsonDocument(),
                After = change.Current?.ToBsonDocument()
            });
        }

        await audit.SaveAsync(context.Transaction, cancellationToken);
    }
}

// Transactional outbox: events are stored with the change, and published only after the commit succeeds.
public sealed class OutboxInterceptor(IOutboxPublisher publisher) : VaultInterceptor
{
    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var document in context.Written<IRaisesEvents>())
        {
            context.Items.GetOrAdd("events", () => new List<object>()).AddRange(document.TakeEvents());
        }

        return ValueTask.CompletedTask;
    }

    public override async ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken) =>
        await publisher.PublishAsync(context.Items.Get<List<object>>("events"), cancellationToken);
}

public interface IOutboxPublisher
{
    Task PublishAsync(IReadOnlyList<object> events, CancellationToken cancellationToken);
}
#endif
