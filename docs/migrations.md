# Migrations

A migration is a change to a vault's data or schema, with a version. Each is applied once, in version order, and
recorded; applied migrations can be reverted down to a version.

## Writing one

```csharp
public sealed class ExpireEndedPolicies(TimeProvider clock) : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 1, 0);

    public string Description => "Expire active policies whose end date has passed";

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // Every agency's policies, deleted ones included.
        context.Vault.Policies
            .Without(MultiTenancyFeature.Key)
            .Without(SoftDeleteFeature.Key)
            .UpdateMany(p => p.Status == PolicyStatus.Active && p.EndsAt < now,
                Builders<Policy>.Update.Set(p => p.Status, PolicyStatus.Expired));

        await context.Vault.SaveAsync(cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

- A migration is created through DI, in a scope of its own, so it can take services.
- `context.Vault` is the vault, from that scope: its saves join the migration's transaction. Writes it queues and doesn't
  save would be lost, as would changes to documents it tracks, so a migration that leaves either fails.
- `context.Database` and `context.Session` are for the driver directly, such as a schema change on raw documents:

```csharp
public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
    context.Database.GetCollection<BsonDocument>("policies").UpdateManyAsync(context.Session,
        Builders<BsonDocument>.Filter.Exists("premium"),
        Builders<BsonDocument>.Update.Rename("premium", "Premium"),
        cancellationToken: cancellationToken);
```

### Outside a transaction

A migration runs in a transaction unless it opts out with `UseTransaction => false`, for what MongoDB doesn't allow in
one, such as building an index, or dropping a collection.

```csharp
public sealed class DropLegacyIndex : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 2, 0);

    public bool UseTransaction => false;

    public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Vault.Policies.MongoCollection.Indexes.DropOneAsync("legacy_number", cancellationToken);

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

### Declarations in a migration

A collection's [indexes and settings](configuration.md#indexes) are declared on the collection, and the migrator applies
them: settings before the migrations, indexes after. A migration applies them at a point of its own through
`context.Collection(...)`, outside its transaction, which it opts out of:

| `DeclaredCollection<T>` | |
|---|---|
| `ApplyIndexesAsync()`, `ApplyIndexesAsync(names)` | The declared indexes, or those named, set up as the migrator does: for the migrations after this one to run on them |
| `RecreateIndexAsync(name)` | Drops the index of the name, and any the server would refuse the declared one beside, then creates it as declared: for what MongoDB can't change in place |
| `CreateAsync()` | The collection created with its declared settings, encrypted if it has encrypted members: for a migration that rebuilds it |

```csharp
public sealed class MakePolicyNumbersUnique : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 3, 0);

    public bool UseTransaction => false;

    public async Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken)
    {
        // Duplicates removed first, then the index, now declared unique, rebuilt.
        await RemoveDuplicateNumbersAsync(context, cancellationToken);
        await context.Collection(x => x.Policies).RecreateIndexAsync("PolicyNumber_1", cancellationToken);
    }

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

Called in a migration's transaction, they throw. A name the collection no longer declares, such as one renamed since the
migration was written, is logged and left to the declarations the migrator applies after the migrations. For an index
the vault doesn't declare, `CreateOrUpdateIndexAsync` and `RecreateIndexAsync` on a driver collection do the same, with
an index declared as a collection declares one:

```csharp
await context.Database.GetCollection<AuditEntry>("audit").CreateOrUpdateIndexAsync(
    keys => keys.Ascending(x => x.At), index => index.ExpireAfter(TimeSpan.FromDays(90)), cancellationToken);
```

## Declaring them

```csharp
vault.Migrations(m => m.AddFromAssemblyOf<PolicyVault>()); // every IVaultMigration<PolicyVault> in that assembly

vault.Migrations(m => m
    .Add<CreatePolicyIndexes>()
    .Add<ExpireEndedPolicies>()
    .CollectionName("policy_migrations")); // where they're recorded; "migrations" by default
```

Two migrations of a vault with the same version, or two vaults recording theirs in one collection, fail with
`VaultConfigurationException`.

## Applying them

`IVaultMigrator`, a singleton, applies them:

```csharp
var migrator = app.Services.GetRequiredService<IVaultMigrator>();

// Every registered vault, to its [MongoVersion] or its highest: at startup, before serving requests.
await migrator.MigrateAllAsync();

// One vault, the same way, or to a target.
await migrator.MigrateAsync<PolicyVault>();
await migrator.MigrateAsync<PolicyVault>(new SemVersion(1, 1, 0));

// Without waiting until search indexes are queryable.
await migrator.MigrateAllAsync(new MigrateOptions { WaitForSearchIndexes = false });

// With the indexes exactly what the vaults declare: recreated, and those no vault declares dropped.
await migrator.MigrateAllAsync(new MigrateOptions { SyncIndexes = true });

SemVersion? version = await migrator.GetVersionAsync<PolicyVault>();
```

### Pinning a vault's version

`[MongoVersion]` on a vault names the version `MigrateAllAsync`, and `MigrateAsync<TVault>()` without a target, migrate it
to, instead of its highest. A release can then ship migrations ahead of the data, and going back is a release with a
lower version: the migrations above it are reverted.

```csharp
[MongoVersion("2.0.0")]
public sealed class PolicyVault : MongoVault, IConfigurableVault<PolicyVault> { ... }
```

It must be a semantic version, on a vault that declares migrations, which is checked at startup; and the version of one
of its migrations, which is checked when it's migrated. A target passed to `MigrateAsync` wins over it.

### What migrating does

Migrating to a target reverts the applied migrations above it, newest first, with their `DownAsync`, then applies the
migrations up to it that haven't been applied, oldest first, including ones added below the current version: reverting
first brings the data back to the target's shape before those apply to it. A
migration is recorded in its own transaction, so one that fails rolls back with its record and leaves the ones before it
applied; outside a transaction, what it did before failing stays. Either way it throws `MigrationFailedException`, with
the failure inside.

Run migrations from one instance of the app. Instances migrating at once can apply a migration twice: one in a
transaction fails to record the second time and rolls back, but one without a transaction has already run.

A vault that encrypts, with MongoFlow.Encryption, has its encrypted collections set up before its migrations run, even
when it has none: created encrypted with their data keys, or rebuilt encrypted when they hold documents already. See
[Queryable Encryption](encryption.md#setting-up-the-collections).

Collections that declare settings are set up next, before the migrations: created with them, or changed in place where
they differ. Declared indexes are set up after the migrations, which may remove what a unique index would refuse, or
rename the fields an index is on: created, or changed in place where they differ. Either fails, naming each, before it
changes anything, when one differs in what MongoDB can't change in place. With `MigrateOptions.SyncIndexes`, indexes and
search indexes are synced instead, once every vault's migrations have run: what MongoDB can't change in place is
recreated, and what no vault declares dropped. See [indexes](configuration.md#indexes),
[syncing them](configuration.md#syncing-indexes) and [collection settings](configuration.md#collection-settings).

A vault whose collections declare search indexes has them set up after its migrations, even when it has none: created,
or updated when they're declared otherwise, then waited for until they're queryable, unless `MigrateOptions` says not to.
See [MongoDB Search](search.md#setting-up-indexes), which also has helpers for search indexes a migration sets up by hand.

Migrations log under `MongoFlow.Migrations`, collections under `MongoFlow.Collections` and indexes under
`MongoFlow.Indexes`, and are traced as `MongoFlow.Migrate` and `MongoFlow.Migration` spans, collections, indexes and search
indexes as `MongoFlow.Collection`, `MongoFlow.Index` and `MongoFlow.SearchIndex` spans; see
[observability](observability.md).

## From 0.5

The history 0.5 recorded, in each vault's `migrations` collection, carries over: versions applied by 0.5 count as
applied. A vault's `[MongoVersion]` still names the version it migrates to. A history that records a version twice, as
0.5 instances migrating at once could leave, still migrates, with a warning: its unique index can't be built until the
duplicate records are removed.

0.5 ran only the migrations above the current version; 1.0 runs every migration up to the target that hasn't been
applied. A migration 0.5 skipped, because it was added below a version already applied, runs on the first migrate after
upgrading. Before upgrading, compare each vault's migrations with its history, and record or remove the ones that
shouldn't run.
