# Interceptors

An interceptor takes part in every save of the collections it applies to. It derives from `VaultInterceptor` and
overrides the hooks it needs:

| Hook | Runs | For |
|---|---|---|
| `SavingAsync` | Before the write | Changing, adding or removing writes, guarding them with conditions, or rejecting the save by throwing |
| `SavedAsync` | After the write, before the commit | Reading each write's result; writing to other vaults in the same transaction |
| `CommittedAsync` | After the transaction commits | Work that must only follow a commit, such as signalling a dispatcher |
| `FailedAsync` | When the save's writes are rolled back | Undoing what the interceptor did in memory |

`SavingAsync` runs in registration order, defaults' interceptors first. The other hooks run in reverse, like middleware,
so the interceptor that ran last before the write runs first after it. The built-in concurrency token runs last before
the write, so it sees writes as they'll be sent, such as a delete soft delete turned into an update.

A save whose `SavingAsync` throws writes nothing. One whose `SavedAsync` throws is rolled back; see
[transactions](transactions.md#when-a-save-fails). An exception from `FailedAsync` is logged and ignored, so the save's
own failure surfaces, and so is one from `CommittedAsync`, since the writes are committed: the other interceptors' hooks
still run. `FailedAsync` runs only for a save whose interceptors started.

## Registering

```csharp
// By type: created from the request's services once per vault instance, so it can take scoped services, and
// disposed with the scope.
vault.AddInterceptor<AuditTrailInterceptor>();

// An instance, shared by every request. MongoFlow doesn't dispose it.
vault.AddInterceptor(new ClaimDecisionGuard());

// Only some collections.
vault.AddInterceptor<AuditTrailInterceptor>(interceptor => interceptor
    .For(collection => collection.DocumentType != typeof(AuditEntry)));

// One collection, so it can be written against the document type.
vault.Collection(x => x.Claims, claims => claims.AddInterceptor(new ClaimDecisionGuard()));
```

An interceptor added by a [feature](features.md) belongs to it: it doesn't see writes queued with the feature off, and
switches off with it.

## The save

Each hook gets a `SaveContext`:

| Member | What it is |
|---|---|
| `Operations` | The save's writes in queue order, minus those the interceptor doesn't see. Every interceptor works on the same list. |
| `Replace(operation, replacement)`, `Remove(operation)` | Change the list, during `SavingAsync` |
| `Result` | The save's counts, once written |
| `Session` | The session of the transaction the save runs in |
| `Services` | The request's services |
| `Vault` | The vault being saved |
| `Items` | State the interceptor keeps between hooks of one save, keyed by itself or anything it owns |

To add a write during `SavingAsync`, queue it on the vault's collections as usual: it joins the save, and interceptors
that run later see it. A write another task queues meanwhile waits for the next save, so every interceptor sees it.

Each write is an `InsertOperation<T>`, `ReplaceOperation<T>`, `UpdateOperation<T>` or `DeleteOperation<T>`, with its
`Kind`, `Collection`, `Document` when there's one, its key or filter, and after the write its own `Result`. An update's
`WithUpdate(update)` and a delete's `ToUpdate(update)` give a copy with another update definition, to put in its place.

A write that brings a [tracked](change-tracking.md) document up to date also has its `Original`: the document as it was
before the save, as a `RawBsonDocument`; see
[the document before the save](change-tracking.md#the-document-before-the-save).

## Examples

### Timestamps, in the update itself

Registered per collection, an interceptor knows the document type, so it can add to update definitions, not only to
documents in memory:

```csharp
public sealed class TimestampInterceptor<TDocument>(TimeProvider clock) : VaultInterceptor
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var operation in context.Operations)
        {
            switch (operation)
            {
                case InsertOperation<TDocument> { Document: ITimestamped inserted }:
                    inserted.CreatedAt = now;
                    break;

                case UpdateOperation<TDocument> update:
                    var stamp = Builders<TDocument>.Update.Set(x => ((ITimestamped)x!).UpdatedAt, now);
                    context.Replace(update, update.WithUpdate(Builders<TDocument>.Update.Combine(update.Update, stamp)));
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}
```

`Operations` is a snapshot, taken again once it changes, so replacing while going through it is safe.

### Write conditions

`AddCondition` adds a filter the stored document must match too, checked by the server in the write itself, so two
requests can't both pass it. A write whose condition fails matches nothing and the save goes on: the interceptor decides
what that means in `SavedAsync`.

```csharp
/// <summary>A decided claim stays decided.</summary>
public sealed class ClaimDecisionGuard : VaultInterceptor
{
    private static readonly FilterDefinition<Claim> StillOpen = Builders<Claim>.Filter.Eq(c => c.Status, ClaimStatus.Open);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            if (operation is UpdateOperation<Claim> { IsSetBased: false } update)
            {
                update.AddCondition(StillOpen);
            }
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            if (operation is UpdateOperation<Claim> { IsSetBased: false, Result.Matched: 0 } update)
            {
                throw new ClaimAlreadyDecidedException((Guid)update.Key!);
            }
        }

        return ValueTask.CompletedTask;
    }
}
```

Conditions from several interceptors are joined with AND. An insert matches no stored document, so it can't take one.

### An audit trail in the same transaction

`SavedAsync` runs inside the save's transaction, and a save of another vault from there joins it, so the audit entries
commit or roll back with the change they describe:

```csharp
public sealed class AuditTrailInterceptor(IAuditVault audit, ICurrentUser user) : VaultInterceptor
{
    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            audit.Entries.Add(new AuditEntry
            {
                Collection = operation.Namespace.CollectionName,
                Action = operation.Kind.ToString(),
                UserId = user.Id
            });
        }

        await audit.SaveAsync(cancellationToken);
    }
}
```

A vault can't save itself from its own interceptors; the audit vault skips the defaults that add this interceptor, so it
doesn't audit its own writes. With change tracking, an entry can hold the document before the change too, from the
operation's `Original`, without reading it back.

### A transactional outbox

Events are stored in `SavedAsync`, in the save's transaction, and the dispatcher is woken in `CommittedAsync`, once
they're committed. `Items` carries what the first hook found to the second:

```csharp
public sealed class OutboxInterceptor(IOutboxVault outbox, IOutboxSignal signal) : VaultInterceptor
{
    private static readonly object Stored = new();

    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var events = context.Operations.Select(o => o.Document).OfType<IRaisesEvents>().SelectMany(d => d.TakeEvents()).ToList();
        if (events.Count == 0)
        {
            return;
        }

        outbox.Messages.AddRange(events.Select(OutboxMessage.From));
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
```

The [samples](https://github.com/InsurUp/MongoFlow/tree/main/samples/MongoFlow.Samples) run each of these.
