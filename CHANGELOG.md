# Changelog

Notable changes to MongoFlow. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

## [1.0.0-beta.1] - Unreleased

A redesign. The vault is the one entry point, each setting has one place, and a save is one client bulk write in a
transaction. Almost every public type changed; [Moving from 0.5](#moving-from-05) maps the old API to the new one.

### Requirements

- MongoDB 8.0 or later, as a replica set: a save is one client-level bulk write, in a transaction.
- .NET 10 or .NET 11, and MongoDB.Driver 3.12.

### Added

- `AddMongoVault<TVault>` and `AddMongoVault<TInterface, TVault>`, configured through `IVaultBuilder<TVault>`: the
  database, collections, keys, query filters, interceptors, features and migrations. A vault can configure itself
  (`IConfigurableVault<TSelf>`), and configurations can apply to every vault (`AddDefaultVaultConfiguration`), which a
  vault can skip.
- `IVaultCollection<TDocument>`, and keyed `IVaultCollection<TDocument, TKey>`:
  - reads resolve the query filters with one await and return the driver's own types: `QueryAsync`, `FindAsync` (with an
    expression or the driver's `FilterDefinition`), `AggregateAsync` and `GetByKeyAsync`;
  - writes are queued until `SaveAsync`: `Add`, `AddRange`, `UpdateMany` and `DeleteMany`, plus `Replace`, `Update`,
    `UpdateByKey`, `Delete` and `DeleteByKey` on keyed collections;
  - `Without(FeatureKey)` gives a view with a feature switched off.
- Keys other than `_id`, including composite keys such as `x => new TokenKey(x.UserId, x.Provider)`, matched as BSON.
- Query filters for one collection, or for every collection whose documents implement an interface. They can be static,
  built per query from the request's services, or asynchronous.
- Built-in features:
  - soft delete, by a flag or a timestamp;
  - multi-tenancy, which stamps the current tenant on inserts and replaces, and rejects those, and updates made with a
    document, carrying another tenant;
  - a concurrency token (`UseConcurrencyToken`), which guards replaces, updates and deletes made with a document and
    throws `ConcurrencyException`. Pipeline updates get its increment as a last stage.

  Features of your own implement `IVaultFeature`, and are switched off by their `FeatureKey`.
- Interceptors (`VaultInterceptor`), with `SavingAsync`, `SavedAsync`, `CommittedAsync` and `FailedAsync`:
  - the save's operations are in `SaveContext`, and an interceptor can replace, remove and add writes;
  - `VaultOperation<T>.AddCondition` guards a write with a condition of your own;
  - one registered by type is created once per vault instance, and disposed with the scope.
- Transactions across vaults: `IVaultTransactionManager.BeginAsync`. Every vault saved in the scope joins, and a save
  that fails after writing rolls the whole transaction back.
- `SaveResult`, and an `OperationResult` on every operation.
- Migrations: `vault.Migrations(m => ...)`, `IVaultMigration<TVault>` and `IVaultMigrator`.
  - Each migration runs in a DI scope of its own, in a transaction unless it opts out.
  - Migrations can be reverted down to a target version.
  - The history of 0.5, in the `migrations` collection, carries over.
- Logging through the `ILoggerFactory` in DI, with every event ID in `MongoFlowLogEvents`. It warns about keys and
  feature fields without indexes; MongoFlow doesn't create indexes.
- Tracing and metrics through `System.Diagnostics`, with every name in `MongoFlowTelemetry`. Add
  `.AddSource(MongoFlowTelemetry.ActivitySourceName)` and `.AddMeter(MongoFlowTelemetry.MeterName)` to OpenTelemetry,
  and the driver's `MongoTelemetry.ActivitySourceName` to see the commands MongoFlow sends.
  - Saves and migrations are spans. The driver's spans nest under them, and the driver's span for a begun transaction
    starts under the code that began it. Reads appear only as the driver's spans, since they run after `QueryAsync`
    returns.
  - Instruments: save duration, the writes saves committed, by kind, and how long begun transactions stay open, by
    outcome. Failures are tagged with `error.type`.
  - The meter comes from the `IMeterFactory` in DI, if there is one.
- Change tracking, off by default: with `vault.UseChangeTracking()`, the documents `GetByKeyAsync`, `FindAsync` and
  `QueryAsync` return on keyed collections are tracked, and `SaveAsync` writes what changed in them, with no write queued.
  - Each changed document gets one update by the key it was read with: changed fields are set, removed ones unset,
    embedded documents compared field by field and arrays set whole. Fields stored but not mapped are left alone.
  - `WithTracking()` and `WithNoTracking()` give views that track, or don't, whatever the vault says. Projections and
    aggregations aren't tracked.
  - A queued `Replace` or `Delete` of a tracked document takes the place of its changes. A changed key fails the save.
  - Changes a save wrote are pending again if it fails or its transaction rolls back.
- `MongoVault` is `IDisposable`: its scope gives back the pooled memory it holds, tracked documents' snapshots and
  writes never saved.
- Parallel tasks can share a vault instance to read and queue writes.

### Removed

- `DocumentSet<T>`, `VaultConfigurationManager<T>`, `VaultConfigurationBuilder`, `IVaultConfigurationSpecification`
  and `MongoVaultOptionsBuilder<T>`.
- Interceptor diagnostics (`EnableDiagnostics`): every operation's result is reported.
- `IMongoMigration`, `[MongoVersion]`, `MigrateOnStartup()`, `IMongoVaultMigrationManager` and `MigrateResult`.
- `IMongoVaultTransaction` and `IMongoGlobalTransactionManager`.
- `DisableContext`.

### Moving from 0.5

| 0.5 | 1.0 |
|---|---|
| A `DocumentSet<T>` property | An `IVaultCollection<T>` or `IVaultCollection<T, TKey>` property with `init` |
| A vault constructor taking `VaultConfigurationManager<T>` | No constructor; `AddMongoVault<TVault>(vault => ...)` |
| `IVaultConfigurationSpecification` | `IVaultConfiguration<TVault>`, or the vault's own `IConfigurableVault<TSelf>.Configure` |
| `SaveAsync()` returning `int` | `SaveAsync()` returning `SaveResult` |
| `SavingChangesAsync`, `SavedChangesAsync`, `SaveChangesFailedAsync` | `SavingAsync`, `SavedAsync` (still before the commit), `FailedAsync`; `CommittedAsync` is new |
| `VaultInterceptorContext` | `SaveContext` |
| `AddOperation` and `AddRangeOperation` | `InsertOperation`, one for each document |
| `DisableContext` | `collection.Without(SoftDeleteFeature.Key)` |
| `IMongoMigration` with `Up(db, session)` | `IVaultMigration<TVault>` with `UpAsync(context)` |
| `[MongoVersion]` on the vault | The highest migration is the target; `MigrateAsync<TVault>(version)` for another |
| `MigrateOnStartup()` | `await services.GetRequiredService<IVaultMigrator>().MigrateAllAsync()` at startup |
| `IMongoVaultTransaction` | `IVaultTransactionManager.BeginAsync()` returning `IVaultTransaction` |

### MongoFlow.Identity

The ASP.NET Core Identity provider moved into this repository, and ships with MongoFlow at the same version. It was 0.2.8.

- It's built on this version of MongoFlow: `IdentityMongoVault` has no constructor, and its `Users`, `Roles` and
  `UserTokens` are vault collections, still named after their properties.
- `userManager.Without(FeatureKey)` and `roleManager.Without(FeatureKey)` replace `DisableQueryFilters`,
  `DisableInterceptors`, `DisableAllQueryFilters`, `DisableAllInterceptors`, `DisableMultiTenancy` and
  `DisableSoftDelete`: only features switch off now, such as `Without(MultiTenancyFeature.Key)`.
- Fixed:
  - `FindByIdAsync` threw `NotSupportedException` for `ObjectId` keys, which have no type converter; an id that doesn't
    parse now finds nobody;
  - `FindByLoginAsync` found nobody: Identity looked the user up by the login's user id, which isn't stored;
  - a role manager with filters or interceptors disabled still used the full store;
  - a user changed through the user manager was written twice in one save;
  - a role's id read before it was set threw `NullReferenceException` for reference-type keys.
- `ReplaceClaimAsync` and `RemoveClaimsAsync` change every matching claim, as Entity Framework's store does, not only the
  first.
- `AddMongoFlowStores` checks that the vault's user and role types are Identity's.
- It targets .NET 10 and .NET 11, like MongoFlow.

[1.0.0-beta.1]: https://github.com/InsurUp/MongoFlow/compare/v0.5.7...HEAD
