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
  - reads resolve the query filters with one await and return the driver's own types: `QueryAsync`, `FindAsync`,
    `AggregateAsync` and `GetByKeyAsync`;
  - writes are queued until `SaveAsync`: `Add`, `AddRange`, `UpdateMany` and `DeleteMany`, plus `Replace`, `Update`,
    `UpdateByKey`, `Delete` and `DeleteByKey` on keyed collections;
  - `Without(FeatureKey)` gives a view with a feature switched off.
- Keys other than `_id`, including composite keys such as `x => new TokenKey(x.UserId, x.Provider)`, matched as BSON.
- Query filters for one collection, or for every collection whose documents implement an interface. They can be static,
  built per query from the request's services, or asynchronous.
- Built-in features:
  - soft delete, by a flag or a timestamp;
  - multi-tenancy;
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

[1.0.0-beta.1]: https://github.com/InsurUp/MongoFlow/compare/v0.5.7...HEAD
