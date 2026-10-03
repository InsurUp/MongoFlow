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
- `QueryAsync`'s results, when the query returns the documents themselves: filtered, sorted, paged or picked with
  `Where`, `OrderBy`, `ThenBy`, `Skip`, `Take`, `Distinct`, `OfType`, `First`, `Single`, `Last`, `ElementAt` or `Sample`.
  A `Select`, even to the document type, isn't tracked: a document missing fields could carry a default key. Nor is a
  join, whose results have its own shape.
- Not aggregations, and not documents passed to `Add` or `Replace`.
- Not documents whose key is null.

Each instance a read returns is tracked by reference; reading a document twice tracks two instances.

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

Tracking serializes each document once when it's read, keeps the bytes in pooled memory until the scope ends, and
serializes each tracked document again when the vault saves; an unchanged one costs a comparison of bytes. Read what
won't change with `WithNoTracking()`. For 1,000 orders read, changed and saved, a tracked save allocates about 2.4 KB
per document more than queuing the same updates by key; see `benchmarks/artifacts/results/` for the numbers.
