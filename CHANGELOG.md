# Changelog

Notable changes to MongoFlow. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

## [1.0.0-beta.5] - 2026-10-04

### Added

- A collection's `QueryFilter` takes a `LambdaExpression`, static, per query or asynchronous, for a filter whose
  parameter type is only known at run time, such as one built for every collection: its parameter can be the document
  type, a type it derives from or implements, or `object`, and it's rewritten over the document type. One that can't
  filter the documents fails with `ArgumentException` when added, or fails its query with `InvalidOperationException`.

## [1.0.0-beta.4] - 2026-10-03

### Added

- `VaultOperation.RenderFilter()` and `RenderUpdate()`: a set-based write's filter and an update's definition,
  rendered with the collection's serializers as the write is sent, so an interceptor for every collection can log an
  `UpdateMany` or a `DeleteMany` without knowing the document type. The filter is the caller's own, without the query
  filters added when the write is sent; an update renders to an array for a pipeline.

## [1.0.0-beta.3] - 2026-10-03

### Changed

- `Key` is on `VaultOperation`, so an interceptor for every collection reads the key a write targets without knowing
  the document type: `null` for an insert, whose key is on its document, and for a set-based write.
  `UpdateOperation<T>.Key` and `DeleteOperation<T>.Key` are now that property; `ReplaceOperation<T>.Key` stays
  non-nullable.

## [1.0.0-beta.2] - 2026-10-03

### Added

- Each 1.x release is also published as `MongoFlow.V1` to GitHub Packages
  (`https://nuget.pkg.github.com/InsurUp/index.json`): the same library and `MongoFlow` namespace under its own package
  id and assembly name, so an app moving from 0.5 module by module can load both in one process. A project that
  references both names 1.x through an alias (`<PackageReference Include="MongoFlow.V1" Aliases="V1" />` and
  `extern alias V1;`); module code keeps `using MongoFlow;`. MongoFlow.Identity has no second identity.
- `VaultOperation.Original`: on the writes that bring a tracked document up to date, its update and a queued `Replace`
  or `Delete` of it, the document as it was before the save, as a `RawBsonDocument`. An audit interceptor can log a
  document before and after a change without reading it back; it's copied only when it's read.

## [1.0.0-beta.1] - 2026-10-03

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
  that fails after writing rolls the whole transaction back. One still open when its scope ends is rolled back.
- `SaveResult`, and an `OperationResult` on every operation.
- Migrations: `vault.Migrations(m => ...)`, `IVaultMigration<TVault>` and `IVaultMigrator`.
  - Each migration runs in a DI scope of its own, in a transaction unless it opts out.
  - Migrations can be reverted down to a target version.
  - `[MongoVersion("2.0.0")]` on a vault, as in 0.5, is the version `MigrateAllAsync` migrates it to, up or down;
    without it, the highest.
  - The history of 0.5, in the `migrations` collection, carries over.
  - Every migration up to the target that hasn't been applied runs, including one added below the current version,
    which 0.5 skipped.
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
- Joins on vault queries: a `QueryAsync` query joined with another collection's, as in
  `join p in await vault.Policies.QueryAsync() on ...`, joins only what that query's filters show.
  - The driver joins only a whole collection, so MongoFlow writes the join as the driver's `Lookup`, with the filters
    inside the `$lookup`.
  - Group joins, joins, left joins and joins in a row work, and the joined query can add `Where`s of its own.
  - A query with a page or a sort can't be joined, and both collections must share a database.
  - `QueryAsync` queries go through MongoFlow's LINQ provider, so the driver's `GetClient()` doesn't work on them.
- `MongoVault` is `IDisposable`: its scope gives back the pooled memory it holds, tracked documents' snapshots and
  writes never saved.
- Parallel tasks can share a vault instance to read and queue writes.

### Removed

- `DocumentSet<T>`, `VaultConfigurationManager<T>`, `VaultConfigurationBuilder`, `IVaultConfigurationSpecification`
  and `MongoVaultOptionsBuilder<T>`.
- Interceptor diagnostics (`EnableDiagnostics`): every operation's result is reported.
- `IMongoMigration`, `MigrateOnStartup()`, `IMongoVaultMigrationManager` and `MigrateResult`.
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
| A join on a filtered set's `AsQueryable()`, which the driver rejects | `join x in await vault.X.QueryAsync()`, run as a `$lookup` |
| `IMongoMigration` with `Up(db, session)` | `IVaultMigration<TVault>` with `UpAsync(context)` |
| `[MongoVersion]` on the vault | Unchanged; it must now be the version of one of the vault's migrations |
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
  - a role's id read before it was set threw `NullReferenceException` for reference-type keys;
  - with `string` keys, tokens were stored with a null `_id`, so a second user's token failed; they get an `ObjectId`
    string.
- `ReplaceClaimAsync` and `RemoveClaimsAsync` on users, and `RemoveClaimAsync` on roles, change every matching claim, as
  Entity Framework's store does, not only the first.
- Updates and deletes of users and roles check their `ConcurrencyStamp`, and updates renew it, as Entity Framework's store
  does: one made after another request changed the document fails with `ConcurrencyFailure`, rather than overwriting it.
  So does one MongoDB rejects as a write conflict, while another request's transaction is changing the document.
- A manager the app registers with `AddUserManager` or `AddRoleManager` is kept; `AddMongoFlowStores` replaces only
  Identity's own.
- Deleting a user deletes its tokens, such as its authenticator key and recovery codes.
- `AddMongoFlowStores` checks that the vault's user and role types are Identity's.
- It targets .NET 10 and .NET 11, like MongoFlow.

[1.0.0-beta.5]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.4...v1.0.0-beta.5
[1.0.0-beta.4]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.3...v1.0.0-beta.4
[1.0.0-beta.3]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.2...v1.0.0-beta.3
[1.0.0-beta.2]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.1...v1.0.0-beta.2
[1.0.0-beta.1]: https://github.com/InsurUp/MongoFlow/compare/v0.5.7...v1.0.0-beta.1
