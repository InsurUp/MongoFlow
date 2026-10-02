# Query filters and features

## Query filters

A query filter applies to every read of a collection, key lookups included, and to every write by key or by filter, so
a write can't reach a document a read couldn't see. Inserts aren't filtered.

```csharp
// On one collection, written against its document type.
vault.Collection(x => x.Customers, customers => customers
    .QueryFilter(c => c.Status != CustomerStatus.Merged));

// On every collection whose documents implement an interface; the others are left alone.
vault.QueryFilter<IArchivable>(x => !x.IsArchived);
```

Each place takes three forms:

```csharp
// Static: the same for every request.
vault.QueryFilter<IPublished>(x => x.PublishedAt != null);

// Built per query, from the request's services.
vault.QueryFilter<IOwnedByUser>(services =>
{
    var userId = services.GetRequiredService<ICurrentUser>().Id;
    return x => x.OwnerId == userId;
});

// Asynchronous, resolved by the await that starts each read.
vault.QueryFilter<IRestricted>(async (services, cancellationToken) =>
{
    var user = services.GetRequiredService<ICurrentUser>();
    var permissions = await user.GetPermissionsAsync(cancellationToken);

    return permissions.Contains("restricted.read") ? _ => true : _ => false;
});
```

A filter that returns `_ => true` leaves the read unfiltered, and `_ => false` matches nothing; both are recognized and
folded away. A collection's filters are joined into one; the static ones once, at startup.

## Features

A feature is configuration with a name: the query filters and interceptors it adds belong to it, and switch off with it.
Soft delete, multi-tenancy and a concurrency token are built in, each with its key on its feature class.

### Switching a feature off

```csharp
// For the reads and writes made through one view.
var everyTenant = await vault.Policies.Without(MultiTenancyFeature.Key).QueryAsync(cancellationToken);
vault.Policies.Without(SoftDeleteFeature.Key).DeleteByKey("P-1001"); // a hard delete

// For one collection, in configuration.
vault.Collection(x => x.Customers, customers => customers.Without(SoftDeleteFeature.Key));
```

A write queued through a view keeps the view's features off, as does an update [change tracking](change-tracking.md)
writes for a document read through it.

### Soft delete

```csharp
vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted)    // a flag
    .UseSoftDelete((IDeletedAt x) => x.DeletedAt);       // or a DateTime? or DateTimeOffset? timestamp
```

Reads see only documents not deleted: the flag isn't true, or the timestamp is null. Every delete becomes an update that
sets the flag, or the timestamp from the `TimeProvider` in DI, or the system clock; `Delete(document)` sets it on the
document too. With `Without(SoftDeleteFeature.Key)`, reads see deleted documents and deletes are real.

The lambda's parameter is typed, `(ISoftDeletable x) => ...`, because C# can't infer one type argument while taking
another explicitly. Each soft delete applies to the collections whose documents implement its type.

### Multi-tenancy

```csharp
vault.UseMultiTenancy(
    (ITenantOwned x) => x.TenantId,
    services => services.GetRequiredService<ICurrentUser>().TenantId,
    allTenants: services => services.GetRequiredService<ICurrentUser>().IsPlatformAdmin);
```

The tenant id can be a struct, nullable on the document, or a reference type such as a string.

- Reads see only the current tenant's documents. With no current tenant, they see only documents without one.
- Inserts and replaces without a tenant get the current one. A document of another tenant fails the save with
  `InvalidOperationException`, as does an update made with one, so a write can't move a document out of the tenant.
- Other updates and deletes are limited by the query filter to the current tenant's documents.
- With no current tenant, or when `allTenants` returns true, writes are neither stamped nor checked; for all tenants,
  reads see every tenant.

### Concurrency token

```csharp
vault.UseConcurrencyToken((IVersioned x) => x.Version); // any number type
```

| Write | Checked | Incremented |
|---|---|---|
| `Replace(document)`, `Update(document, update)`, a tracked document's changes | Yes | Yes |
| `Delete(document)`, soft deletes included | Yes | No |
| `UpdateByKey`, `UpdateMany` | No: there's no value read to check | Yes |
| `DeleteByKey`, `DeleteMany` | No | No |

A checked write applies only while the stored token still has the value on the document. It's incremented in storage and
on the document, so the document can be saved again. A checked write that matches nothing fails the save with
`ConcurrencyException`; its `DocumentExists` tells a document changed since it was read from one that's gone, and its
`Operation` is the write. When a save fails, the tokens it incremented on documents are put back. Pipeline updates get
the increment as a last stage. With `Without(ConcurrencyTokenFeature.Key)`, writes are neither checked nor incremented.

```csharp
try
{
    vault.Policies.Replace(policy);
    await vault.SaveAsync(cancellationToken);
}
catch (ConcurrencyException conflict) when (conflict.DocumentExists)
{
    // Someone saved it since it was read: read it again, and decide.
}
```

### Features of your own

A feature implements `IVaultFeature`, with its key and a `Configure` that adds what it needs:

```csharp
/// <summary>Hides archived documents, and stamps who archived them.</summary>
public sealed class ArchiveFeature : IVaultFeature
{
    public static FeatureKey Key { get; } = new("archive");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter<IArchivable>(x => !x.IsArchived)
        .AddInterceptor<ArchiveStampInterceptor>();
}

vault.AddFeature<ArchiveFeature>(); // created from the root provider; or AddFeature(new ArchiveFeature())
```

A feature can add per-collection rules with `ForEachCollection`, as the built-in ones do, to apply only to the
collections whose documents it concerns. Several features can share a key; switching it off switches them all off.
Built-in keys live on their feature classes: `SoftDeleteFeature.Key`, `MultiTenancyFeature.Key`,
`ConcurrencyTokenFeature.Key`.
