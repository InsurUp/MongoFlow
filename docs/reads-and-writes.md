# Reads and writes

## Reads

Every read starts with one await, which resolves the collection's query filters, some of which may be asynchronous. It
returns the driver's own type with the filters applied, so everything after it is the driver's API.

```csharp
// LINQ: the driver's IQueryable. Its async operators are in MongoDB.Driver.Linq.
var orders = await vault.Orders.QueryAsync(cancellationToken);
var large = await orders.Where(x => x.Total > 100).OrderBy(x => x.Id).ToListAsync(cancellationToken);

// Find, with an expression or a filter built with the driver.
var find = await vault.Orders.FindAsync(x => x.Customer == "ada", cancellationToken);
var recent = await find.SortByDescending(x => x.Id).Limit(10).ToListAsync(cancellationToken);
var tagged = await vault.Orders.FindAsync(Builders<Order>.Filter.AnyEq(x => x.Tags, "rush"), cancellationToken);

// An aggregation, whose first stage matches the query filters.
var aggregate = await vault.Orders.AggregateAsync(cancellationToken);
var totals = await aggregate.Group(x => x.Customer, g => new { Customer = g.Key, Total = g.Sum(x => x.Total) })
    .ToListAsync(cancellationToken);

// By key, on a keyed collection: null if there's none the query filters let the caller see.
var order = await vault.Orders.GetByKeyAsync(42, cancellationToken);
```

Inside an open transaction, reads run in its session, so they see its writes.

`MongoCollection` is the driver's collection: what's read or written through it bypasses query filters, features,
interceptors and the vault's save. Use it for what MongoFlow doesn't cover, such as indexes or change streams.

### Views

A collection returns views of itself, for one read or write:

- `Without(FeatureKey)` switches a feature off, such as `Without(SoftDeleteFeature.Key)` to see deleted documents; see
  [features](features.md).
- `WithTracking()` and `WithNoTracking()` decide whether reads track what they return; see
  [change tracking](change-tracking.md).

A view of a keyed collection is keyed too, and views chain: `vault.Orders.Without(MultiTenancyFeature.Key).WithNoTracking()`.

Code that only knows the document type reaches a collection with `vault.Collection<Order>()`, or
`vault.Collection<Order, int>()` for a keyed one.

## Writes

Writes are queued on the vault, and nothing is sent until `SaveAsync`.

| Method | Collections | Queues |
|---|---|---|
| `Add(document)`, `AddRange(documents)` | All | One insert per document. Adding the same instance twice inserts it once. |
| `UpdateMany(filter, update)` | All | An update of every document the filter matches |
| `DeleteMany(filter)` | All | A delete of every document the filter matches |
| `Replace(document)` | Keyed | A replace of the stored document with the same key |
| `Update(document, update)` | Keyed | An update by the document's key. A concurrency token is checked against the document's. |
| `UpdateByKey(key, update)` | Keyed | An update by key, without reading the document first |
| `Delete(document)` | Keyed | A delete by the document's key, checked against its concurrency token |
| `DeleteByKey(key)` | Keyed | A delete by key, without reading the document first |

Updates take the driver's `UpdateDefinition`, from `Builders<T>.Update`. A write by key or by filter also carries the
collection's query filters, so it can't reach a document a read couldn't see: an agent's `DeleteByKey` can't delete
another tenant's document.

With [change tracking](change-tracking.md), documents read and then changed are written without any of these.

## Saving

```csharp
vault.Orders.Add(order);
vault.Orders.UpdateByKey(7, Builders<Order>.Update.Set(x => x.Status, OrderStatus.Shipped));
vault.Invoices.Add(invoice);

SaveResult result = await vault.SaveAsync(cancellationToken);
// result.Inserted == 2, result.Matched == 1, result.Modified == 1, result.Deleted == 0
```

`SaveAsync` sends every queued write as one ordered client bulk write, in queue order, across the vault's collections.
It runs in the scope's open [transaction](transactions.md) if there is one, and in a transaction of its own otherwise, so
nothing is written if any write fails. Queued writes are discarded once the save ends, whether it succeeded or failed.
A save with nothing queued returns `SaveResult.Empty` without starting a transaction.

Each write's own result is on its operation, which [interceptors](interceptors.md) see. A save fails with:

- the driver's `ClientBulkWriteException` when the server rejects a write, such as a duplicate key;
- `ConcurrencyException` when a write guarded by a [concurrency token](features.md#concurrency-token) finds the document
  changed or gone;
- `InvalidOperationException` when it's called from one of the vault's own interceptors, while another save of the scope
  runs, or inside a transaction on another client.

## Parallel use

A vault instance belongs to its scope, and can be shared by the scope's parallel tasks within these limits:

- Writes can be queued from parallel tasks. One queued while a save runs waits for the next save, unless one of the
  save's interceptors queued it.
- Reads can run in parallel while no transaction is open in the scope.
- The saves of a scope's vaults run one after another: a save that starts while another runs fails with
  `InvalidOperationException`, unless the running save's interceptors started it.
- Inside a transaction, including the one a save opens while it runs, reads and saves share one session, and MongoDB runs
  a transaction's operations one at a time: run them one after another.
- With change tracking, don't change tracked documents while a save runs: it reads them to compare.

```csharp
// No transaction is open: read in parallel, queue from each task, then save once.
var orders = await Task.WhenAll(ids.Select(id => vault.Orders.GetByKeyAsync(id, cancellationToken)));
foreach (var order in orders.OfType<Order>())
{
    vault.Orders.UpdateByKey(order.Id, Builders<Order>.Update.Set(x => x.Status, OrderStatus.Archived));
}

await vault.SaveAsync(cancellationToken);
```

The scope disposes its vault, which gives back the pooled memory it holds: tracked documents' snapshots, and writes
queued but never saved, which are dropped.
