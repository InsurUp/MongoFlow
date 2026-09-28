# MongoFlow samples

A small insurance platform written against MongoFlow's API, to show how the API reads in a real app and what it's still
missing. The library has interfaces but no implementation yet, so the samples compile but don't run.

## Layout

| Folder | What it shows |
|---|---|
| `Domain/` | Tenant-owned, soft-deletable documents, plus permission and module attributes |
| `Vaults/` | A vault that configures itself (`PolicyVault`), a custom string key, a custom struct key, a keyless audit log |
| `Identity/` | A vault base class shipped by a library, with its own setup |
| `Configuration/` | Two defaults, two features driven by attributes with async filters, a naming convention, and a per-vault configuration that reads options |
| `Program.cs` | Registration: defaults, per-vault databases, a second cluster, skipping defaults, opting back in |
| `Services/` | Reads (key lookups, LINQ, find, aggregation, features switched off) and writes (unit of work, key and set-based operations) |
| `Missing/` | Code the app needs that the API can't express yet, inside `#if MISSING_API` |

## Seeing what's missing

```sh
dotnet build samples/MongoFlow.Samples -p:DefineConstants=MISSING_API
```

The compiler errors are the list of missing API. The compiler stops at missing types in signatures before it checks
method bodies, so delete `Missing/Interceptors.cs`, `Missing/Migrations.cs` and `Missing/ClaimService.cs` from a copy
to see the rest.

The missing parts are grouped by the phase that adds them.

## Gaps

### Configuration (phase 4, except interceptors and transactions, which come with phase 3)

1. **Interceptors:** `AddInterceptor<T>(i => i.NeedsOriginals().For(...))`. Designed, not written.
2. **Indexes:** per collection, including the unique index a custom key almost always needs, and when they're created.
3. **Collection settings and creation options:** write concern, time-series, capped, validation.
4. **Migrations per vault:** `vault.Migrations(...)`.
5. **Optimistic concurrency:** a version member that makes a replace fail if the document changed.
6. **Composite keys:** `UserToken` is looked up by user and provider together.
7. **Transactions across vaults:** registration, and the requirement that the vaults share a client.
8. **Soft delete by timestamp:** `UseSoftDelete` only takes a `bool` member.
9. **Multi-tenancy:**
   - String tenant ids aren't allowed, because `UseMultiTenancy` requires a struct.
   - There's no way to let platform admins see every tenant. Returning `null` means "documents without a tenant".
10. **Defaults shipped by a library for a generic base vault:** a default takes one type parameter, and
    `IVaultCollection<TUser, ...>` is invariant, so the identity package's setup has to be applied by hand.

### Save pipeline (phase 3)

11. **Transactions:** per vault, and across vaults with `IVaultTransactions`, including saving inside one.
12. **Interceptor API:**
    - A save context over the shared operation list.
    - `Saving`, `Saved`, `Committed` and `Failed` hooks.
    - Writing to another vault inside the same transaction.
    - Registering interceptors per vault or per collection.
13. **The built-in features' write side:** soft delete turning deletes into updates, and multi-tenancy setting the
    tenant on inserts.
14. **Migration API:** `IVaultMigration<TVault>`, a migration context (vault, database, session, ensure collections and
    indexes), and `IVaultMigrator.MigrateAllAsync`.
15. **Collections by type at runtime:** core's authorization interceptor looks up a parent document whose type is only
    known at runtime; that needs a way to reach a vault's collection without its property.

### Friction in code that compiles today

- **Feature keys:** `IVaultFeature.Key` is an instance property, but callers need the key without an instance to
  switch a feature off, so every feature declares a second, static key under another name.
- **Built-in shortcuts need a typed lambda parameter,** `UseSoftDelete((ISoftDeletable x) => x.IsDeleted)`. C# can't
  infer one type argument and accept another explicitly, so `UseSoftDelete<ISoftDeletable>(...)` isn't possible.
- **Database prefix:** a per-environment prefix is repeated in every registration, because there's deliberately no
  name hook.
- **Keyless documents need `[BsonIgnoreExtraElements]`** to read back the server's `_id`; MongoFlow should check this
  at startup.
- **Strongly typed ids** like `AgencyId` need serializers registered outside MongoFlow. It's undecided whether MongoFlow
  should offer a place for them.
