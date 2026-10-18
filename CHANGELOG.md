# Changelog

Notable changes to MongoFlow. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/).

## [1.0.0-beta.16] - 2026-10-18

### Added

- `SearchField.Multi(name, alternate)`, which gives a text mapping an alternate: the text indexed again as the alternate
  analyzes it, which a query picks by name with `path: { value, multi }`, such as a `lucene.keyword` one for `wildcard`
  to match the whole value. Each call adds one, written as `multi` is by hand, so an index declared with `Definition`
  and moved to `Map` matches the one on the server, rather than being rebuilt. As MongoDB Search does, it refuses
  alternates on a mapping that isn't text, and alternates that aren't text or have alternates of their own.

## [1.0.0-beta.15] - 2026-10-18

### Added

- `MigrateOptions.SyncIndexes`, off by default, for the server's indexes to be exactly what the code declares. Once every
  vault's migrations have run, the migrator syncs each collection a migrated vault declares with what every registered
  vault declares of it, so vaults that map one collection keep each other's indexes. Beside creating what's missing and
  changing in place what MongoDB can, it makes a plain index declared unique unique in place, leaving it as it was while
  documents share its keys; recreates one that differs in what MongoDB can't change in place, or that the server keeps
  under another name; recreates a search index declared of the other type; and drops, last, the indexes and search
  indexes no vault declares, but `_id_` and Queryable Encryption's on `__safeContent__`. It plans every collection before
  it changes any, and fails, changing nothing, when two vaults declare an index of one name otherwise, or when an index
  it would drop is the only one on a sharded collection's shard key, which it reads with `$listClusterCatalog`, from
  MongoDB 8.0.10. Each index it drops is logged as `IndexDropped`, under `MongoFlow.Indexes` or `MongoFlow.Search`, with
  its definition, and traced as `dropped` or `recreated`.
- `CreateCollection()` among a collection's settings: the migrator creates the collection before the vault's migrations,
  as the server would, though it declares nothing else, so two saves making the first write to it at once don't fail with
  "Collection namespace ... is already in use". One that exists is left as it is.

### Fixed

- On mongodb-community-search, which lists an index's definition as it was created, without the defaults it fills in or
  the index's type: a declared search index whose mappings leave a default unset matches it, rather than being updated,
  and rebuilt, on every run; and a vector search index, declared or searched by name, is known as one, rather than
  failing the vault's second migration, or the search, as a search index.

## [1.0.0-beta.14] - 2026-10-18

### Fixed

- A declared search index matches its definition as self-managed mongot (MongoDB Community) lists it, with the index's
  name, type and number of partitions at its root. Before, no declared index matched there, so the migrator updated
  every one on every run and mongot rebuilt each. A declaration that sets another number of partitions still differs.

## [1.0.0-beta.13] - 2026-10-17

### Added

- MongoDB Search, on Atlas and on MongoDB Community with mongot. A collection declares its indexes with
  `SearchIndex(name, ...)`, mapping members as `SearchField.String()`, `Autocomplete()`, `Token()` and the rest, and
  `VectorSearchIndex(name, ...)`; MongoFlow maps the fields its features filter on, such as the tenant and soft delete's
  flag, as their values compare. `SearchAsync`, `SearchMetaAsync` and `VectorSearchAsync` filter with the collection's
  query filters in the index, where the index maps a field to compare exactly, and match the rest right after the search
  stage, so a search finds only what a read would; one that can't count what the query filters hide, counting, faceting
  or returning stored source, throws instead. `SearchFiltersAsync` gives the query filters as search clauses, for a
  pipeline of your own. Searches run outside the scope's transaction, and a collection with encrypted members can't be
  searched, nor declare a search index. It logs under `MongoFlow.Search`.
- Declared indexes: `Index(keys => keys.Ascending(x => x.Number), index => index.Unique())`, with the driver's typed key
  builder and `IIndexBuilder<T>`'s name, unique, sparse, partial filter, expiry, hidden and collation. An index is known by
  its name, given or generated as the server generates one, so an index created without a name matches its declaration.
- Declared collection settings: `Settings(s => s.Collation(...).Validator(x => ...).ChangeStreamPreAndPostImages()
  .Clustered(...))`, each layered, so a vault's own wins over a default configuration's. Capped and time series
  collections can't be declared, as the server refuses writes to them in a transaction, which every save is in.
- `IVaultMigrator` sets up what collections declare: their settings before the vault's migrations, creating a collection
  with them; their indexes and search indexes after, which migrations may prepare, such as by removing what a unique index
  would refuse. It changes in place what MongoDB can, an index's expiry and visibility, a collection's validator, change
  stream images and expiry, and a search index's definition, and waits until search indexes are queryable unless
  `MigrateOptions.WaitForSearchIndexes` says not to, through the new `MigrateAllAsync(options)` and
  `MigrateAsync<TVault>(target, options)`. It fails, naming each, before it changes anything, when a collection or an
  index differs in what MongoDB can't change in place, such as an index's keys or whether it's unique, or a collection's
  collation. Indexes nothing declares are left. It logs under `MongoFlow.Indexes` and `MongoFlow.Collections`, and traces
  `MongoFlow.Collection`, `MongoFlow.Index` and `MongoFlow.SearchIndex` spans under `MongoFlow.Migrate`.
- `MigrationContext.Collection(x => x.Orders)`, a `DeclaredCollection<T>`, for a migration to apply what a collection
  declares at a point of its own, outside its transaction: `ApplyIndexesAsync`, `RecreateIndexAsync`, for a change
  MongoDB can't make in place, and `CreateAsync`, for a collection rebuilt anew. For indexes a vault doesn't declare,
  `CreateOrUpdateIndexAsync` and `RecreateIndexAsync` on `IMongoCollection<T>`, and for search indexes
  `CreateOrUpdateAsync` and `WaitUntilQueryableAsync` on `IMongoSearchIndexManager`.

### Changed

- The index check counts the indexes a vault declares, hidden ones aside, as there, as the migrator creates them, so it
  doesn't warn about one about to be created.
- MongoFlow.Encryption creates an encrypted collection with the settings it declares. A rebuild creates it without its
  validator, which documents stored before it may not pass, and the migrator adds the validator once they're copied. An
  index, partial filter or validator on an encrypted member fails the model. A rebuild of a collection with search
  indexes warns that they follow its documents to `{name}.unencrypted`, where they keep them indexed in plain text.

## [1.0.0-beta.12] - 2026-10-16

### Added

- MongoFlow.Encryption, a new package shipped with MongoFlow at the same version: MongoDB Queryable Encryption for a
  vault, on MongoDB Community too. `vault.UseQueryableEncryption(qe => qe.KeyVault(...).Kms(...))` names where the data
  keys are kept and the KMS that protects them, typed: `LocalKms`, `AwsKms`, `AzureKms`, `GcpKms` or `KmipKms`, with
  credentials from the environment through `FromEnvironment` and `FromManagedIdentity`. A collection declares what's
  encrypted with `Encrypt(x => x.Ssn, EncryptedQuery.Equality)`, `EncryptedQuery.Range(min, max)`, or no query for a
  member that's only stored encrypted. MongoFlow encrypts with the driver's explicit encryption: documents are stored with
  their encrypted members encrypted, and null ones left out; `$set` encrypts what it sets; and the values the filters it
  renders compare encrypted members with, LINQ `Where` clauses included, are encrypted. What MongoDB can't do to an
  encrypted member throws `NotSupportedException` instead of writing or silently matching nothing: `$inc`, a pipeline
  update, a sort or group on one, or a filter after projecting one. `UpdateMany`, `DeleteMany` and soft delete write each
  matching document in turn, as the server changes encrypted documents only one at a time. The vault reads and writes
  through a client made from the one it was given, which vaults with the same settings share. The model fails to build
  when a member can't be encrypted, such as the key or a feature's member, or when the documents don't ignore extra
  elements, as the server adds `__safeContent__` to each.
- `IVaultMigrator` sets a vault's encrypted collections up before its migrations: it creates them with their data keys,
  checks existing ones against what the vault declares, and rebuilds as encrypted a collection that holds documents
  already, which MongoDB can't encrypt in place: renamed to `{name}.encrypting`, copied in batches into the encrypted
  collection, its indexes that don't cover an encrypted member copied too, then kept as `{name}.unencrypted`, which
  MongoFlow never drops. An interrupted rebuild goes on where it stopped. It logs under `MongoFlow.Encryption`.

### Changed

- A vault on a client with `AutoEncryptionOptions`, which the driver refuses client bulk writes, saves with collection
  commands, in queue order, in the same transaction: inserts into one collection that follow each other in one, every
  other write in a command of its own. Through such a client, `DuplicateKeyException`'s inner exception is the driver's
  `MongoWriteException` or `MongoBulkWriteException` rather than `ClientBulkWriteException`.
- A vault on another client than the scope's open transaction's fails with a message saying that a vault that encrypts
  uses a client of its own, which vaults with the same encryption settings share.

## [1.0.0-beta.11] - 2026-10-16

### Added

- `IMongoVault.Reset()`: starts the vault instance over, as a new scope's would be. It forgets every document the vault
  tracks, giving its snapshot back, so the next read tracks afresh and the next save compares nothing read before, and
  discards the writes queued on it. For running a unit of work again in the same scope, such as a retry after a
  `TransientTransactionError`: the changes a failed save leaves pending, made to what the failed attempt read, would
  otherwise be written with the retry's. Nothing stored changes, and the scope's open transaction is left as it is. It
  fails with `InvalidOperationException` while a save of the vault runs, from its own interceptors too. A class that
  implements `IMongoVault` itself, rather than deriving from `MongoVault`, implements it too.

## [1.0.0-beta.10] - 2026-10-16

### Changed

- With change tracking, a document a save inserts with `Add` or replaces with `Replace` is tracked once the save writes
  it, as if it had been read back, with its BSON as it is after the save's interceptors, such as a stamped tenant or an
  incremented concurrency token. A later change in the same scope is written as an update of what changed, and a later
  `Replace` or `Delete` works as for any tracked document. It's tracked when the view the write was queued through
  tracks, as a read through it would be, so an `Add` through `WithNoTracking()` stays untracked. A document without a
  key, a replace that matched nothing, and a document another write of the same save updates or deletes stay untracked,
  and a save or transaction that rolls back forgets what it started tracking. Code that changes an added document after
  its save without meaning to write it now writes it on the next save: add it through `WithNoTracking()`.

## [1.0.0-beta.9] - 2026-10-16

### Added

- `SaveContext.Tracked`: the documents the vault tracked when the save started, changed or not, in the order they were
  read, minus those the interceptor doesn't see. The unchanged ones aren't written; the changed ones are among
  `Operations` too. For what an interceptor needs from documents whether or not they changed, such as domain events
  they hold outside their BSON.
- `IVaultBuilder.RunSaveWhen<TDocument>(pending)`: a save with nothing to write runs its interceptors, in its
  transaction, when a tracked document assignable to `TDocument` matches `pending`, with no `Operations` and a `Result`
  of zero counts, so what they write through `context.Session`, or queue on the vault, commits. Only such a save, of a
  vault with interceptors, checks it, so a vault without it pays nothing.

## [1.0.0-beta.8] - 2026-10-15

### Added

- Reads by filter in one call on every collection: `ToListAsync`, `FirstOrDefaultAsync`, `FirstAsync`,
  `SingleOrDefaultAsync`, `SingleAsync`, `AnyAsync` and `CountAsync`, each taking an expression or a
  `FilterDefinition<T>`. Each is `FindAsync(filter)` followed by the driver's operator of the same name, so query
  filters, the transaction, views and change tracking apply as they do to `FindAsync`; `CountAsync` is
  `CountDocumentsAsync`. They're extension methods (`VaultCollectionReadExtensions`), so `IVaultCollection<T>` and fakes
  of it don't change.
- `DuplicateKeyException`, which a save throws when a unique index refuses a write. Its `Operation` is the refused write,
  so a save writing to two collections with unique indexes tells which one it was.

### Changed

- A save whose write a unique index refuses throws `DuplicateKeyException` rather than the driver's
  `ClientBulkWriteException`, which is now its `InnerException`, with the server's error and its labels. Code that
  catches `ClientBulkWriteException` for duplicate keys catches `DuplicateKeyException` instead; other refused writes
  still throw `ClientBulkWriteException`. Like `ConcurrencyException`, it derives from `Exception`, so a
  `catch (MongoException)` no longer catches a duplicate key.

## [1.0.0-beta.7] - 2026-10-07

### Added

- `IMongoVault.Collection(Type)` and `KeyedCollection(Type)`: a vault's collections without their type arguments, as
  `IVaultCollection` and `IKeyedVaultCollection`, for code that has a document type only as a `Type`, such as an
  interceptor for every collection. They take the same writes, and the keyed one reads by key, with documents and keys
  as `object`s and filters and updates as BSON, as an operation's `RenderFilter()` and `RenderUpdate()` give them. Each
  call queues what the typed call queues. A document, key or update of the wrong kind fails with `ArgumentException`
  when it's queued, and `KeyedCollection` fails for a keyless collection.

### Changed

- `UpdateOperation<T>.Filter` and `DeleteOperation<T>.Filter` are `FilterDefinition<T>?` rather than
  `Expression<Func<T, bool>>?`, since an untyped write's filter is BSON. A typed write's is the driver's
  `ExpressionFilterDefinition<T>`, whose `Expression` is the lambda; `RenderFilter()` renders either.

## [1.0.0-beta.6] - 2026-10-06

### Changed

- `InsertOperation<T>.Key` is the inserted document's key, read with the collection's key, such as `PolicyNumber` for
  a collection keyed by `Key(x => x.PolicyNumber)`, rather than `null`, so an interceptor for every collection reads an
  insert's key as it reads any other write's. It's read from the document each time, so an `ObjectId` the write fills
  in shows after the write; a keyless collection's inserts still give `null`. The trace log of each write now shows an
  insert's key too.

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

[1.0.0-beta.16]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.15...v1.0.0-beta.16
[1.0.0-beta.15]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.14...v1.0.0-beta.15
[1.0.0-beta.14]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.13...v1.0.0-beta.14
[1.0.0-beta.13]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.12...v1.0.0-beta.13
[1.0.0-beta.12]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.11...v1.0.0-beta.12
[1.0.0-beta.11]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.10...v1.0.0-beta.11
[1.0.0-beta.10]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.9...v1.0.0-beta.10
[1.0.0-beta.9]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.8...v1.0.0-beta.9
[1.0.0-beta.8]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.7...v1.0.0-beta.8
[1.0.0-beta.7]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.6...v1.0.0-beta.7
[1.0.0-beta.6]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.5...v1.0.0-beta.6
[1.0.0-beta.5]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.4...v1.0.0-beta.5
[1.0.0-beta.4]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.3...v1.0.0-beta.4
[1.0.0-beta.3]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.2...v1.0.0-beta.3
[1.0.0-beta.2]: https://github.com/InsurUp/MongoFlow/compare/v1.0.0-beta.1...v1.0.0-beta.2
[1.0.0-beta.1]: https://github.com/InsurUp/MongoFlow/compare/v0.5.7...v1.0.0-beta.1
