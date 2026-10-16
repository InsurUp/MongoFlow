# MongoFlow

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/vpre/MongoFlow)](https://www.nuget.org/packages/MongoFlow)

A unit of work for MongoDB on .NET, built on the official driver. A vault holds a database's collections: reads return the
driver's own types with your query filters applied, and writes are queued until `SaveAsync`, which sends them as one
client bulk write, in a transaction. Soft delete, multi-tenancy, a concurrency token, interceptors, change tracking,
migrations and MongoDB Search come with it, it logs, traces and measures what it does, and MongoFlow.Encryption adds
Queryable Encryption, on MongoDB Community too.

> **1.0 is in beta.** Feedback is welcome in [the issues](https://github.com/InsurUp/MongoFlow/issues).
> [The changelog](https://github.com/InsurUp/MongoFlow/blob/main/CHANGELOG.md#moving-from-05) maps 0.5's API to 1.0's.

## Requirements

- MongoDB 8.0 or later, as a replica set: a save is one client bulk write, in a transaction. Through a client that
  encrypts, which the driver refuses client bulk writes, it's a command per write, in the same transaction.
- .NET 10 or .NET 11, and MongoDB.Driver 3.12.

## Installation

```bash
dotnet add package MongoFlow --prerelease
```

## Quick start

A vault declares its collections as properties. A keyed collection is looked up by key: by default the member the driver
maps to `_id`.

```csharp
public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class ShopVault : MongoVault
{
    public IVaultCollection<Order, int> Orders { get; init; } = null!;
}
```

Register it with the database it uses, and resolve it from DI; it's scoped, like a request.

```csharp
services.AddSingleton<IMongoClient>(new MongoClient("mongodb://localhost:27017/?replicaSet=rs0"));
services.AddMongoVault<ShopVault>(vault => vault
    .UseDatabase("shop")
    .UseSoftDelete((Order x) => x.IsDeleted));
```

Reads start with one await, which resolves the query filters, and return the driver's types. Writes are queued, and
written when the vault is saved.

```csharp
public sealed class OrderService(ShopVault vault)
{
    public async Task<List<Order>> LargeAsync(CancellationToken cancellationToken)
    {
        var orders = await vault.Orders.QueryAsync(cancellationToken);

        return await orders.Where(x => x.Total > 100).ToListAsync(cancellationToken);
    }

    // Both writes go in one bulk write, in a transaction: the new order is stored and the old one deleted, or neither.
    public async Task ReplaceAsync(int oldOrderId, Order newOrder, CancellationToken cancellationToken)
    {
        vault.Orders.Add(newOrder);
        vault.Orders.DeleteByKey(oldOrderId); // soft delete turns this into an update that sets IsDeleted

        await vault.SaveAsync(cancellationToken);
    }

    public async Task DiscountAsync(int orderId, decimal amount, CancellationToken cancellationToken)
    {
        vault.Orders.UpdateByKey(orderId, Builders<Order>.Update.Inc(x => x.Total, -amount));
        await vault.SaveAsync(cancellationToken);
    }
}
```

## What it does

### Configuration

Each vault is configured where it's registered, by the vault itself (`IConfigurableVault<TSelf>`), or by configurations
that apply to every vault (`AddDefaultVaultConfiguration`), which a vault can skip. Collections are selected by their
property, so a wrong key type or a collection the vault doesn't declare is a compile error. Everything is applied and
validated once, when the vault is first resolved or migrated.

```csharp
services.AddMongoVault<IPolicyVault, PolicyVault>(vault => vault
    .UseDatabase("policies")
    .Collection(x => x.Policies, policies => policies
        .Name("insurance_policies")
        .Key(p => p.PolicyNumber)));
```

[Configuration](https://github.com/InsurUp/MongoFlow/blob/main/docs/configuration.md): registration, keys and composite
keys, defaults, and the order settings apply in.

### Reads and writes

`QueryAsync`, `FindAsync`, `AggregateAsync` and `GetByKeyAsync` read, and reads by filter such as `ToListAsync(filter)`
and `AnyAsync(filter)` take one call; `Add`, `AddRange`, `Replace`, `Update`, `UpdateByKey`, `UpdateMany`, `Delete`,
`DeleteByKey` and `DeleteMany` queue writes. A write by key or filter also carries the query filters, so it can't reach a
document a read couldn't see. A query joined with another collection's query joins only what that query's filters show.
`MongoCollection` is the driver's collection, with nothing applied.

[Reads and writes](https://github.com/InsurUp/MongoFlow/blob/main/docs/reads-and-writes.md): joins, what a save sends, its
result, how it fails, and what may run in parallel.

### Change tracking

With `vault.UseChangeTracking()`, the documents reads return are tracked, and so are the documents a save inserts or
replaces, once written. `SaveAsync` writes what changed in them: one update by key, setting the changed fields and
nothing else.

```csharp
var policy = await vault.Policies.GetByKeyAsync("P-1001");
policy!.Status = PolicyStatus.Cancelled;
await vault.SaveAsync(); // { $set: { Status: "Cancelled" } }
```

Interceptors read every tracked document, changed or not, from `SaveContext.Tracked`, and `RunSaveWhen` runs them for a
save with nothing else to write, for what documents hold outside their BSON, such as domain events. `vault.Reset()`
forgets what the vault tracks and queued, to run a unit of work again in the same scope after a failed save.

[Change tracking](https://github.com/InsurUp/MongoFlow/blob/main/docs/change-tracking.md).

### Transactions

A save runs in a transaction of its own, or joins the scope's open one. `IVaultTransactionManager.BeginAsync` opens one
that every vault saved in the scope joins, so saves of several vaults commit together, or roll back together.

```csharp
await using var transaction = await transactions.BeginAsync();
policies.Claims.Add(claim);
await policies.SaveAsync();
customers.Customers.UpdateByKey(customerId, Builders<Customer>.Update.Inc(x => x.OpenClaims, 1));
await customers.SaveAsync();
await transaction.CommitAsync();
```

[Transactions](https://github.com/InsurUp/MongoFlow/blob/main/docs/transactions.md): rollback, and what a failed save
does to an open transaction.

### Query filters and features

Query filters apply to every read and write by key or filter: static, built per query from the request's services, or
asynchronous. Features bundle filters and interceptors behind a `FeatureKey`, so they can be switched off for one read
or one collection. Soft delete, multi-tenancy and a concurrency token are built in.

```csharp
vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
    .UseMultiTenancy((ITenantOwned x) => x.TenantId, services => services.GetRequiredService<ITenant>().Id)
    .UseConcurrencyToken((IVersioned x) => x.Version);

var everything = await vault.Orders.Without(MultiTenancyFeature.Key).QueryAsync();
```

[Query filters and features](https://github.com/InsurUp/MongoFlow/blob/main/docs/features.md).

### Interceptors

A `VaultInterceptor` sees a save's writes before they're sent and their results after, and can replace, remove or add
writes, or guard one with a condition the server checks. Its hooks run before the write, after it, after the commit, and
when the save fails.

[Interceptors](https://github.com/InsurUp/MongoFlow/blob/main/docs/interceptors.md): hooks, write conditions, and an
audit trail and an outbox written in the same transaction.

### Migrations

Migrations are classes with a version, applied once, in order, each in a transaction unless it opts out, and recorded.
They can be reverted down to a version.

```csharp
await app.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
```

[Migrations](https://github.com/InsurUp/MongoFlow/blob/main/docs/migrations.md).

### MongoDB Search

Search indexes are declared on the collection; the migrator creates them, keeps them as declared and waits until they're
queryable. Searches and vector searches filter with the query filters in the index, a tenant and soft delete's flag
included, so they find only what a read would.

```csharp
.Collection(x => x.Customers, customers => customers
    .SearchIndex("default", index => index.Map(x => x.Name, SearchField.String(), SearchField.Autocomplete()))
    .VectorSearchIndex("vectors", index => index.Vector(x => x.Embedding, dimensions: 1536, VectorSimilarity.Cosine)));

var adas = await (await vault.Customers.SearchAsync(Builders<Customer>.Search.Text(x => x.Name, "ada"))).ToListAsync();
```

[MongoDB Search](https://github.com/InsurUp/MongoFlow/blob/main/docs/search.md): what's filtered in the index and what
after it, `$searchMeta`, vector search, pipelines of your own, indexes managed by hand, and Queryable Encryption.

### Logging, tracing and metrics

MongoFlow logs through the `ILoggerFactory` in DI, and warns about keys and feature fields no index covers. It traces
saves, migrations and the search indexes it sets up, and measures saves and transactions, through `System.Diagnostics`,
for OpenTelemetry.

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(MongoFlowTelemetry.ActivitySourceName, MongoTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(MongoFlowTelemetry.MeterName));
```

[Logging, tracing and metrics](https://github.com/InsurUp/MongoFlow/blob/main/docs/observability.md).

### ASP.NET Core Identity

[MongoFlow.Identity](https://github.com/InsurUp/MongoFlow/blob/main/src/Identity/README.md) stores Identity's users and
roles in a vault, where your features and interceptors apply to them too.

### Queryable Encryption

[MongoFlow.Encryption](https://github.com/InsurUp/MongoFlow/blob/main/src/Encryption/README.md) stores the members you
declare encrypted, and encrypts the values your filters compare them with, so they stay queryable. MongoFlow encrypts
them itself, so it works on MongoDB Community. The migrator creates the encrypted collections and their keys, and
rebuilds as encrypted a collection that holds documents already.

```csharp
services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase("crm")
    .UseQueryableEncryption(qe => qe
        .KeyVault("encryption", "__keyVault")
        .Kms(new LocalKms(masterKey)))
    .Collection(x => x.Customers, customers => customers
        .Encrypt(x => x.Ssn, EncryptedQuery.Equality)
        .Encrypt(x => x.Salary, EncryptedQuery.Range(0, 1_000_000))));

var customer = await vault.Customers.FirstOrDefaultAsync(x => x.Ssn == ssn);
```

[Queryable Encryption](https://github.com/InsurUp/MongoFlow/blob/main/docs/encryption.md): query types, setting up and
rebuilding collections, what can and can't be queried or written, and clients and transactions.

## Samples

[The samples](https://github.com/InsurUp/MongoFlow/tree/main/samples/MongoFlow.Samples) are a small insurance platform
that runs: `dotnet run --project samples/MongoFlow.Samples` starts MongoDB in Docker and walks through every feature
above, as requests would.

## Contributing

Issues and pull requests are welcome. The tests need Docker for their MongoDB; `.claude/rules/` describes how tests and
benchmarks are written.

## License

MIT; see [LICENSE.md](https://github.com/InsurUp/MongoFlow/blob/main/LICENSE.md).
