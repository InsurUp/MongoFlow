# MongoFlow.Encryption

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/vpre/MongoFlow.Encryption)](https://www.nuget.org/packages/MongoFlow.Encryption)

MongoDB Queryable Encryption for [MongoFlow](https://github.com/InsurUp/MongoFlow) vaults. The members you declare are
stored encrypted, and the values your filters compare them with are encrypted too, so they stay queryable. MongoFlow
encrypts them itself, with the driver's explicit encryption, so it works on MongoDB Community. It ships with MongoFlow,
at the same version.

## Installation

```bash
dotnet add package MongoFlow.Encryption --prerelease
```

## Usage

Declare what's encrypted, and how it's queried, where the vault is registered:

```csharp
services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase("crm")
    .UseQueryableEncryption(qe => qe
        .KeyVault("encryption", "__keyVault")
        .Kms(new LocalKms(masterKey)))
    .Collection(x => x.Customers, customers => customers
        .Encrypt(x => x.Ssn, EncryptedQuery.Equality)                 // ==, !=, Contains
        .Encrypt(x => x.Salary, EncryptedQuery.Range(0, 1_000_000))  // <, <=, >, >= too
        .Encrypt(x => x.Notes)));                                      // encrypted, not queryable
```

Run the migrator at startup, before the vault is used. It creates the encrypted collections and their data keys, and
rebuilds as encrypted a collection that holds documents already, keeping its documents unencrypted in
`{name}.unencrypted` until you drop it:

```csharp
await app.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
```

Then read and write as with any vault:

```csharp
var customer = await vault.Customers.FirstOrDefaultAsync(x => x.Ssn == ssn);

vault.Customers.UpdateByKey(customer!.Id, Builders<Customer>.Update.Set(x => x.Ssn, newSsn));
await vault.SaveAsync();
```

Encrypted documents must ignore extra elements (`[BsonIgnoreExtraElements]`): the server adds `__safeContent__` to each.

## More

[Queryable Encryption](https://github.com/InsurUp/MongoFlow/blob/main/docs/encryption.md) covers query types, setting up
and rebuilding collections, what can and can't be queried or written, and clients and transactions.
