# Change tracking

With change tracking, a document a read returns is kept with a copy of its BSON. `SaveAsync` serializes it again,
compares the two, and writes what changed, without a write being queued:

```csharp
var policy = await vault.Policies.GetByKeyAsync("P-1001", cancellationToken);
policy!.Status = PolicyStatus.Cancelled;
policy.Address.City = "London";

await vault.SaveAsync(cancellationToken);
// One update by key: { $set: { Status: "Cancelled", "Address.City": "London" } }
```

## Switching it on

It's off by default. `UseChangeTracking()` switches it on for every keyed collection of the vault; keyless collections
aren't tracked, since an update needs a key.

```csharp
public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
    .Collection(x => x.Policies, policies => policies.Key(p => p.PolicyNumber))
    .UseChangeTracking();
```

`UseChangeTracking(false)` switches it off for a vault a default configuration switched it on for. For one read, a view
says otherwise:

```csharp
// The vault tracks, but a list that won't change doesn't need to.
var policies = await vault.Policies.WithNoTracking().QueryAsync(cancellationToken);

// The vault doesn't track, but this read does.
var policy = await vault.Policies.WithTracking().GetByKeyAsync("P-1001", cancellationToken);
```

## What's tracked

- `GetByKeyAsync`'s result.
- `FindAsync`'s results, unless the find is projected, with `Project`, `As` or a projection set on its options.
- What the reads by filter return, such as `ToListAsync(filter)` and `FirstOrDefaultAsync(filter)`; `AnyAsync` and
  `CountAsync` return no documents.
- `QueryAsync`'s results, when the query returns the documents themselves: filtered, sorted, paged or picked with
  `Where`, `OrderBy`, `ThenBy`, `Skip`, `Take`, `Distinct`, `OfType`, `First`, `Single`, `Last`, `ElementAt` or `Sample`.
  A `Select`, even to the document type, isn't tracked: a document missing fields could carry a default key. Nor is a
  join, whose results have its own shape.
- A document passed to `Add` or `Replace`, once a save writes it; see
  [documents a save writes whole](#documents-a-save-writes-whole).
- Not aggregations.
- Not documents whose key is null.

Each instance a read returns is tracked by reference; reading a document twice tracks two instances.

### Documents a save writes whole

A document a save inserts with `Add`, or replaces with `Replace`, is tracked once the save writes it, as if it had been
read back. Its BSON is taken as it is after the save's interceptors, so a tenant they stamped or a concurrency token
they incremented is part of it. A later change in the same scope is written as an update of what changed, and a later
`Replace` or `Delete` works as it does for any tracked document:

```csharp
var invoice = new Invoice { Id = invoiceId, Status = InvoiceStatus.Pending };
vault.Invoices.Add(invoice);
await vault.SaveAsync(cancellationToken);

invoice.Status = InvoiceStatus.Issued;
await vault.SaveAsync(cancellationToken); // { $set: { Status: "Issued" } }
```

It's tracked if the view the write was queued through tracks, as a read through it would be: an `Add` through
`WithNoTracking()` isn't, and one through `WithTracking()` is, on a vault that doesn't track. It isn't tracked either when:

- its key is null;
- the replace matched nothing, so nothing was written;
- another write of the same save updates or deletes it, such as `Update(document, update)`, whose definition stores
  what the document doesn't hold;
- the save fails, or the transaction it joined rolls back: a rolled-back insert isn't compared against later.

A document that was already tracked stays tracked as before.

## What a save writes

Each changed document gets one update, by the key it was read with:

- a changed or added field is set, and a removed one, such as a null member with `[BsonIgnoreIfNull]`, is unset;
- an embedded document is compared field by field, so its changed fields are set by path, such as `"Address.City"`;
- an array is set whole when anything in it changed;
- a field stored but not mapped by the class, which the class ignores on reads, is left alone; a `Replace` would drop it.

A name no path can hold, such as `a.b` or `$a`, sets the embedded document that holds it whole; at the top level, the
document is replaced instead.

The update carries the features switched off on the view that read the document, so a document read with
`Without(SoftDeleteFeature.Key)` is updated with soft delete off. It's an ordinary operation to
[interceptors](interceptors.md), with the document on it, and the [concurrency token](features.md#concurrency-token)
checks and increments it like any update made with a document.

Tracked updates go before the queued writes, so a document changed and then deleted with `DeleteByKey` in the same save
is updated first.

### The document before the save

A save already holds each tracked document as it was before the save, to compare it with: as it was read, or as the last
save wrote it. Interceptors get it as the operation's `Original`, a `RawBsonDocument`, on the writes that bring a
tracked document up to date: its update, and a queued `Replace` or `Delete` of it, including the update soft delete
makes of a delete. It's `null` on every other write, such as an `UpdateByKey`, or a `Replace` of a document read without
tracking.

```csharp
public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
{
    foreach (var operation in context.Operations)
    {
        if (operation is UpdateOperation<Policy> { Original: { } before } update)
        {
            audit.Log(before, update.Document, update.Update);
        }
    }

    return ValueTask.CompletedTask;
}
```

It's serialized with the collection's serializer, so fields the class doesn't map aren't in it. It's copied the first
time it's read, so saves no interceptor reads it in pay nothing. Read it in the save's hooks, `CommittedAsync` and
`FailedAsync` included: once they've run, an original nobody read can't be read any more, while one that was read stays.

## Documents that didn't change

Interceptors read every document the vault tracks from `SaveContext.Tracked`, changed or not, as it was tracked when the
save started, in the order they were read. The changed ones are among `Operations` too, as are those a queued `Replace`
or `Delete` writes; the unchanged ones aren't written. One an earlier save deleted, in a transaction still open, isn't
there, and an interceptor sees only those of the collections it applies to, read with its feature on.

That's for what a document holds outside its BSON, such as the domain events an aggregate collects: a method that raises
an event without changing a stored field leaves the document with nothing to write. A save with nothing to write returns
before running any interceptor, so the vault says which tracked documents still need them:

```csharp
vault.RunSaveWhen((IDomainEventSet x) => x.HasEvents);
```

A save with nothing else to write then runs its interceptors, in its transaction, if a tracked document of a collection
whose documents are `IDomainEventSet`s has events: `SavingAsync`, `SavedAsync` and `CommittedAsync`, with no
`Operations` and a `Result` of zero counts. What they write through `context.Session`, or queue on the vault during
`SavingAsync`, commits with it, and nothing is written for the document itself. Only a save with nothing else to write,
of a vault with interceptors, checks it, so a vault without it pays nothing; collections whose documents don't match are
left alone, so a default configuration can add it to every vault. A save that writes runs its interceptors anyway, with
the unchanged documents in `Tracked`.

[A transactional outbox](interceptors.md#a-transactional-outbox) reads its events this way.

## Rules

- A key can't change: a save that finds a tracked document's key changed fails with `InvalidOperationException` before
  writing anything. Delete the document and add it with the new key.
- A queued `Replace` or `Delete` of a tracked document takes the place of its changes. After a delete, it isn't tracked.
  A queued `Update(document, update)` doesn't: its tracked changes are written too, before it.
- Once a save writes a document's changes, later saves compare against what it wrote, in the same transaction too. If
  the save fails, or the transaction it joined rolls back, the changes are pending again, and the next save writes them.
- Don't set a concurrency token by hand on a tracked document: the save would set it and increment it in one update,
  which the server rejects.

## Cost

Tracking serializes each document once when it's read, or once a save writes it whole, keeps the bytes in pooled memory
until the scope ends, and serializes each tracked document again when the vault saves; an unchanged one costs a
comparison of bytes. A save of a vault with interceptors also keeps a list of the tracked documents, for `Tracked`. Read
what won't change with `WithNoTracking()`, and add through it what won't change once saved, such as a bulk import. For
1,000 orders read, changed and saved, a tracked save allocates about 2.4 KB per document more than queuing the same
updates by key; see `benchmarks/artifacts/results/` for the numbers.
