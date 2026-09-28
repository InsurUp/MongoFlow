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
| `Services/` | Reads (key lookups, LINQ, find, aggregation, features switched off), writes (unit of work, key and set-based operations), and a transaction spanning two vaults |
| `Interceptors/` | Timestamps (typed, per collection, adding to update definitions), an audit trail (needs originals, writes to another vault in the same transaction, switchable as a feature), and a transactional outbox |
| `Missing/` | Code the app needs that the API can't express yet, inside `#if MISSING_API` |

## Seeing what's missing

```sh
dotnet build samples/MongoFlow.Samples -p:DefineConstants=MISSING_API
```

The compiler errors are the list of missing API. The compiler stops at missing types in signatures before it checks
method bodies, so delete `Missing/Migrations.cs` from a copy to see the rest.

## Gaps

Everything left is phase 4 work: schema, keys and the remaining feature variants.

1. **Indexes:** per collection, including the unique index a custom key almost always needs, and when they're created.
2. **Collection settings and creation options:** write concern, time-series, capped, validation.
3. **Migrations:** `vault.Migrations(...)`, `IVaultMigration<TVault>`, a migration context (vault, database, session,
   ensure collections and indexes), and `IVaultMigrator.MigrateAllAsync`.
4. **Optimistic concurrency:** a version member that makes a replace fail if the document changed.
5. **Composite keys:** `UserToken` is looked up by user and provider together.
6. **Soft delete by timestamp:** `UseSoftDelete` only takes a `bool` member.
7. **Multi-tenancy:**
   - String tenant ids aren't allowed, because `UseMultiTenancy` requires a struct.
   - There's no way to let platform admins see every tenant. Returning `null` means "documents without a tenant".
8. **Defaults shipped by a library for a generic base vault:** a default takes one type parameter, and
   `IVaultCollection<TUser, ...>` is invariant, so the identity package's setup has to be applied by hand.

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
