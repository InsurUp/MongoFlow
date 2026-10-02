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
  save would be lost, so a migration that leaves any fails.
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
one, such as creating an index on a collection that has documents, or dropping a collection. Indexes belong in
migrations: MongoFlow doesn't create them.

```csharp
public sealed class CreatePolicyIndexes : IVaultMigration<PolicyVault>
{
    public SemVersion Version { get; } = new(1, 0, 0);

    public bool UseTransaction => false;

    public Task UpAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Vault.Policies.MongoCollection.Indexes.CreateOneAsync(context.Session,
            new CreateIndexModel<Policy>(Builders<Policy>.IndexKeys.Ascending(p => p.PolicyNumber),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

    public Task DownAsync(MigrationContext<PolicyVault> context, CancellationToken cancellationToken) =>
        context.Vault.Policies.MongoCollection.Indexes.DropOneAsync(context.Session, "PolicyNumber_1", cancellationToken);
}
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

Migrating to a target applies the migrations up to it that haven't been applied, oldest first, including ones added
below the current version, and reverts the applied migrations above it, newest first, with their `DownAsync`. A
migration is recorded in its own transaction, so one that fails rolls back with its record and leaves the ones before it
applied; outside a transaction, what it did before failing stays. Either way it throws `MigrationFailedException`, with
the failure inside.

Run migrations from one instance of the app. Instances migrating at once can apply a migration twice: one in a
transaction fails to record the second time and rolls back, but one without a transaction has already run.

Migrations log under `MongoFlow.Migrations`, and are traced as `MongoFlow.Migrate` and `MongoFlow.Migration` spans; see
[observability](observability.md).

## From 0.5

The history 0.5 recorded, in each vault's `migrations` collection, carries over: versions applied by 0.5 count as
applied. A vault's `[MongoVersion]` still names the version it migrates to.
