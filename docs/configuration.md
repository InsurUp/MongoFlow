# Configuration

A vault is configured once, at startup, when it's first resolved. Its configuration is checked then: a mistake fails
with `VaultConfigurationException` before any request uses the vault.

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

Lookups by key expect one document, so a key other than `_id` needs a unique index. MongoFlow doesn't create indexes:
create them in a [migration](migrations.md). It logs a warning for a key no unique index covers; see
[observability](observability.md).

### Keyless collections

A collection without a key can be read, added to and changed with `UpdateMany` and `DeleteMany`, but has no key
operations; calling one is a compile error. The server still gives each document an `_id`, so a keyless document type
must ignore it on reads, with `[BsonIgnoreExtraElements]` or an `Id` member. That's checked at startup.

## Where configuration lives

| Where | For |
|---|---|
| The registration delegate | What depends on the environment, such as the database |
| The vault's own `Configure` (`IConfigurableVault<TSelf>`) | The vault's shape: keys, names, features, migrations |
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
provider, so it can take singletons and options; a scoped dependency fails at startup. Each configuration type applies
once per vault, however often it's added.

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
