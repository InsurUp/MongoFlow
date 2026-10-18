# Configuration

A vault is configured once, when it's first resolved, or at startup when `MigrateAllAsync` runs, which configures every
registered vault. Its configuration is checked then: a mistake fails with `VaultConfigurationException` before the vault
is used. These docs call that failing at startup.

## Registering a vault

```csharp
services.AddMongoVault<ShopVault>(vault => vault.UseDatabase("shop"));

// Resolved as IShopVault too, for code that only needs the interface.
services.AddMongoVault<IShopVault, ShopVault>(vault => vault.UseDatabase("shop"));

// The delegate can take the root provider, for singletons and options.
services.AddMongoVault<ShopVault>((services, vault) =>
    vault.UseDatabase(services.GetRequiredService<IOptions<ShopOptions>>().Value.Database));
```

The vault is scoped: each request gets its own instance, holding its own queued writes and tracked documents. An app
interface extends `IMongoVault`, which has `SaveAsync`:

```csharp
public interface IShopVault : IMongoVault
{
    IVaultCollection<Order, int> Orders { get; }
}
```

`AddMongoVault` also registers `IVaultTransactionManager` (scoped) and `IVaultMigrator` (singleton). Calling it again for
the same vault adds to its configuration.

### The database

`UseDatabase("name")` takes the `IMongoClient` registered in DI; `UseDatabase(database)` takes an `IMongoDatabase`, such
as one from a client registered under a key, for data in another cluster. There's one database per vault.

## Collections

A vault's collections are its public properties of type `IVaultCollection<TDocument>` or
`IVaultCollection<TDocument, TKey>`, with a setter or an `init` accessor, which MongoFlow fills. A vault declares one
collection per document type. Each is named after its property unless configured otherwise.

```csharp
public sealed class InsuranceVault : MongoVault
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    public IVaultCollection<AuditEntry> Audit { get; init; } = null!;
}
```

A collection is configured by selecting its property, so a property the vault doesn't declare, or a key of another type
than the property's, is a compile error:

```csharp
vault
    .Collection(x => x.Policies, policies => policies
        .Name("insurance_policies")
        .Key(p => p.PolicyNumber))
    .Collection(x => x.Audit, audit => audit.Name("audit_log"));
```

### Keys

A keyed collection's documents are looked up, replaced, updated and deleted by key. Without `Key`, the key is the member
the driver maps to `_id`, whose type must be the property's `TKey`; that's checked at startup.

A composite key is a type built from members, such as a record struct; its constructor's arguments are matched to the
members they're built from:

```csharp
public readonly record struct TokenKey(ObjectId UserId, string Provider);

vault.Collection(x => x.Tokens, tokens => tokens.Key(t => new TokenKey(t.UserId, t.Provider)));

var token = await vault.Tokens.GetByKeyAsync(new TokenKey(userId, "google"));
```

A key is matched as stored, so a key that converts its member, such as `x => (object)x.Id`, fails at startup.

Lookups by key expect one document, so a key other than `_id` needs a unique index, which MongoFlow doesn't declare for
you: declare one, as [below](#indexes). It logs a warning for a key no unique index covers; see
[observability](observability.md). The server still gives each document an `_id`, so a document type keyed by other
members needs an `Id` member or `[BsonIgnoreExtraElements]`, as a keyless one does.

### Keyless collections

A collection without a key can be read, added to and changed with `UpdateMany` and `DeleteMany`, but has no key
operations; calling one is a compile error. The server still gives each document an `_id`, so a keyless document type
must ignore it on reads, with `[BsonIgnoreExtraElements]` or an `Id` member. That's checked at startup.

### Indexes

A collection declares its indexes, which `IVaultMigrator.MigrateAllAsync` creates after the vault's
[migrations](migrations.md#what-migrating-does), and keeps as declared. Keys come from the driver's builder, so they're
typed and named as the collection stores them:

```csharp
vault.Collection(x => x.Policies, policies => policies
    .Key(p => p.PolicyNumber)
    .Index(keys => keys.Ascending(p => p.PolicyNumber), index => index.Unique())
    .Index(keys => keys.Ascending(p => p.AgencyId).Descending(p => p.StartsAt))
    .Index(keys => keys.Ascending(p => p.EndsAt), index => index
        .Name("active_by_end")
        .Partial(p => p.Status == PolicyStatus.Active)
        .ExpireAfter(TimeSpan.FromDays(30)))
    .Index(keys => keys.Text(p => p.Notes)));
```

| `IIndexBuilder<T>` | |
|---|---|
| `Name(name)` | Its name; without it, it's named as the server names one, such as `AgencyId_1_StartsAt_-1` |
| `Unique()` | No two documents with the same values for its keys |
| `Sparse()` | Documents without its keys left out |
| `Partial(filter)` | Only the documents the filter matches, a unique index unique among them alone |
| `ExpireAfter(time)` | A TTL index: documents deleted once the date in its one field is older |
| `Hidden()` | Kept up to date but not used by queries, to see what dropping it would do |
| `Collation(collation)` | How it compares strings, rather than as the collection does |

An index is known by its name. On each run the migrator compares what it declares with what the server lists:

- **none of the name:** it's created;
- **one defined as declared:** nothing to do;
- **one whose expiry, or whether it's hidden, differs:** changed in place;
- **one that differs in anything else,** such as its keys or whether it's unique, or one on the same keys and options
  under another name: MongoDB can't change it in place, so the migrator fails, naming each, before it changes any
  index. Recreate it in a migration, with
  [`context.Collection(...).RecreateIndexAsync`](migrations.md#declarations-in-a-migration).

An index the vault doesn't declare is left as it is: drop it in a migration. Building an index blocks the migrator until
it's built. A vault's own declaration takes the place of a default configuration's of the same name, and a feature's
indexes are left out of a collection that's `Without` it. The server refuses `!= null` in a partial filter, which renders
`$ne`: use `Builders<T>.Filter.Exists`. An index on an encrypted member, or a partial filter comparing one, fails at
startup: the server only sees its value encrypted.

### Collection settings

A collection declares what it's created with, which the migrator creates it with before the vault's migrations, so a
migration's first write doesn't create it without them:

```csharp
vault.Collection(x => x.Payments, payments => payments.Settings(settings => settings
    .Collation(new Collation("tr", strength: CollationStrength.Secondary))
    .Validator(p => p.Amount > 0, DocumentValidationLevel.Strict, DocumentValidationAction.Error)
    .ChangeStreamPreAndPostImages()
    .Clustered(expireAfter: TimeSpan.FromDays(365))));
```

| `ICollectionSettingsBuilder<T>` | | On a collection that exists |
|---|---|---|
| `Collation(collation)` | How its queries and indexes compare strings, key lookups included | Fails when it differs |
| `Validator(rule, level, action)` | What every document written must match, also as `Builders<T>.Filter.JsonSchema(...)` | Changed in place |
| `ChangeStreamPreAndPostImages()` | Change streams can show documents as they were before a change, and after | Changed in place |
| `Clustered(expireAfter)` | Documents stored ordered by `_id`, expiring by it if given | Fails unless clustered; expiry changed in place |
| `CreateCollection()` | Created when missing, though nothing else is declared | Left as it is |

A collection that declares no setting, index or search index isn't created by the migrator, but by the first write to
it: a save that creates it in its transaction while another does too fails. `CreateCollection()` has the migrator create
it all the same, as the server would; a convention declares it for every collection of a vault:

```csharp
public sealed class CreateEveryCollection : IVaultCollectionConfiguration
{
    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection) =>
        collection.Settings(settings => settings.CreateCollection());
}

vault.ForEachCollection(new CreateEveryCollection());
```

A setting it doesn't declare is left as the server has it. Its indexes have its collation unless they declare their own.
MongoDB sets a collection's collation and clustering when it's created, so one that differs fails the migrator: rebuild
it in a migration, creating it anew with [`context.Collection(...).CreateAsync`](migrations.md#declarations-in-a-migration).
Capped and time series collections can't be declared: the server refuses writes to them in a transaction, which every
save is in. A collection with encrypted members is created encrypted with its settings; see
[Queryable Encryption](encryption.md#setting-up-the-collections).

## Where configuration lives

| Where | For |
|---|---|
| The registration delegate | What depends on the environment, such as the database |
| The vault's own `Configure` (`IConfigurableVault<TSelf>`) | The vault's shape: keys, names, indexes, features, migrations |
| An `IVaultConfiguration<TVault>` | Setup that needs services, such as options, or that several apps share |
| A default configuration | Rules for every vault, such as the platform's features or a naming convention |

### The vault configures itself

```csharp
public sealed class PolicyVault : MongoVault, IPolicyVault, IConfigurableVault<PolicyVault>
{
    public IVaultCollection<Policy, string> Policies { get; init; } = null!;

    public static void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, policies => policies.Key(p => p.PolicyNumber))
        .UseConcurrencyToken((Policy p) => p.Version)
        .Migrations(m => m.AddFromAssemblyOf<PolicyVault>());
}
```

### Configurations

An `IVaultConfiguration<TVault>` is applied with `UseConfiguration<TConfiguration>()`, which creates it from the root
provider, so it can take singletons and options. Don't give it scoped services: the root provider resolves one for the
app's life, and fails only when scopes are validated, as in Development. Each configuration type applies once per vault,
however often it's added.

```csharp
public sealed class CustomerVaultConfiguration(IOptions<CustomerOptions> options) : IVaultConfiguration<CustomerVault>
{
    public void Configure(IVaultBuilder<CustomerVault> vault) => vault
        .Collection(x => x.Customers, customers =>
        {
            if (options.Value.HardDelete)
            {
                customers.Without(SoftDeleteFeature.Key);
            }
        });
}

services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase("customers")
    .UseConfiguration<CustomerVaultConfiguration>());
```

### Defaults for every vault

A default is an open generic configuration, closed over each vault type. Its constraints decide which vaults it applies
to: a vault that doesn't satisfy them is skipped.

```csharp
public sealed class PlatformDefaults<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault) => vault
        .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
        .AddInterceptor<AuditInterceptor>();
}

services.AddDefaultVaultConfiguration(typeof(PlatformDefaults<>));
```

A default's rules only touch the collections whose documents they concern, so vaults without soft-deletable documents
are unaffected by `UseSoftDelete`. A default can select collections through its constraints: with
`where TVault : MongoVault, IAuditedVault`, `vault.Collection(x => x.Audit, ...)` resolves to the vault's own property.

A vault opts out with `SkipDefaultConfiguration<PlatformDefaults<ThisVault>>()`, or of every default with
`SkipAllDefaultConfigurations()`, then back in to some with `UseConfiguration`. A default can't skip others.

### Conventions

`ForEachCollection` calls a configuration with each collection's typed builder, for rules that depend on the document
type or the property:

```csharp
public sealed class SnakeCaseNames : IVaultCollectionConfiguration
{
    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection) =>
        collection.Name(ToSnakeCase(collection.PropertyName));
}

vault.ForEachCollection(new SnakeCaseNames());
```

## The order settings apply in

Defaults apply first, then the vault's own `Configure`, then the registration delegate. A setting with one value, such
as a collection's name or `UseChangeTracking`, takes the vault's own value over a default's. Lists, such as query
filters, features and interceptors, keep every entry, defaults' first. Where the calls sit doesn't matter:
`AddDefaultVaultConfiguration` can come after `AddMongoVault`.

## Registrations a vault needs from the driver

MongoFlow leaves serialization to the driver. Register a serializer for each strongly typed id, and a `Guid`
representation before the first vault is resolved:

```csharp
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
BsonSerializer.RegisterSerializer(new AgencyIdSerializer());
```
