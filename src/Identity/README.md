# MongoFlow.Identity

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/MongoFlow.Identity)](https://www.nuget.org/packages/MongoFlow.Identity)

A MongoDB provider for ASP.NET Core Identity, built on [MongoFlow](https://github.com/InsurUp/MongoFlow). Users and roles
are stored in a MongoFlow vault, so its query filters, features and interceptors apply to them like to any other
collection. It ships with MongoFlow, at the same version.

## Installation

```bash
dotnet add package MongoFlow.Identity
```

## Usage

Derive the app's vault from `IdentityMongoVault`, register it, and call `AddMongoFlowStores` after `AddRoles`:

```csharp
public sealed class AppVault : IdentityMongoVault;

services.AddMongoVault<AppVault>(vault => vault.UseDatabase("app"));

services.AddIdentityCore<MongoUser>()
    .AddRoles<MongoRole>()
    .AddMongoFlowStores<AppVault>();
```

The vault has three collections, `Users`, `Roles` and `UserTokens`, named after their properties unless the vault's
configuration names them. A user is one document, with its claims, logins, role ids and passkeys inside; tokens are
documents of their own.

### Your own users, roles or keys

```csharp
public sealed class AppUser : MongoUser
{
    public string? DisplayName { get; set; }
}

public sealed class AppRole : MongoRole
{
    public string? Description { get; set; }
}

public sealed class AppVault : IdentityMongoVault<AppUser, AppRole, ObjectId>;
```

`IdentityMongoVault<TUser>` and `IdentityMongoVault<TUser, TKey>` cover the cases with plain roles. The driver fills in a
missing `ObjectId` when a user or role is created; with another key type, set the id first or give the driver an id
generator for it.

### Switching a feature off

The managers `AddMongoFlowStores` registers can make a copy whose reads and writes have a vault feature switched off,
such as to reach every tenant's users, or deleted ones:

```csharp
var allTenants = userManager.Without(MultiTenancyFeature.Key);
var user = await allTenants.FindByEmailAsync(email);

var deletedRoles = roleManager.Without(SoftDeleteFeature.Key);
```
