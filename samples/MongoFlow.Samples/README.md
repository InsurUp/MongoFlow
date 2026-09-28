# MongoFlow samples

A small insurance platform written against MongoFlow's API, to show how the API reads in a real app. The library has
interfaces but no implementation yet, so the samples compile but don't run.

## Layout

| Folder | What it shows |
|---|---|
| `Domain/` | Tenant-owned, soft-deletable documents, plus permission and module attributes |
| `Vaults/` | Vaults that configure themselves: a custom string key with a concurrency token and indexes, soft delete by timestamp, a time-series audit log, a custom struct key |
| `Identity/` | A vault base class shipped by a library that configures every vault derived from it, including a composite key |
| `Configuration/` | Two defaults, two features driven by attributes with async filters, a naming convention, and a per-vault configuration that reads options |
| `Program.cs` | Registration (defaults, per-vault databases, a second cluster, skipping defaults) and migrating every vault at startup |
| `Migrations/` | A data fix through the vault, a schema change with the driver, and a migration outside a transaction |
| `Services/` | Reads (key lookups, LINQ, find, aggregation, features switched off), writes (unit of work, key and set-based operations), and a transaction spanning two vaults |
| `Interceptors/` | Timestamps (typed, per collection, adding to update definitions), an audit trail (needs originals, writes to another vault in the same transaction, switchable as a feature), and a transactional outbox |

## Gaps

None left in the API as far as these samples reach. What's left is the implementation (phase 5) and tests (phase 6).
When a new scenario needs something the API can't express, write it the way it should read inside `#if MISSING_API`;
building with `-p:DefineConstants=MISSING_API` then lists what's missing.

## Friction that remains

- **Built-in shortcuts need a typed lambda parameter,** `UseSoftDelete((ISoftDeletable x) => x.IsDeleted)`. C# can't
  infer one type argument and accept another explicitly, so `UseSoftDelete<ISoftDeletable>(...)` isn't possible.
- **Compound index keys need `using MongoDB.Driver;`,** since chaining `.Ascending(...)` is a driver extension method.
- **Database prefix:** a per-environment prefix is repeated in every registration, because there's deliberately no
  name hook.
- **Strongly typed ids** like `AgencyId` need serializers registered with the driver; MongoFlow leaves that to it.
- **A library's base vault configures derived vaults through a self-referencing type parameter**
  (`UserVault<TSelf, TUser>`), which apps then see in their vault declaration.
