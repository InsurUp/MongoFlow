# Queryable Encryption

[MongoDB Queryable Encryption](https://www.mongodb.com/docs/manual/core/queryable-encryption/) stores chosen fields
encrypted, so the server, its logs and its backups never hold them in plain text, while queries can still compare them.
MongoFlow.Encryption brings it to a vault: you declare which members are encrypted and how they're queried, and MongoFlow
encrypts them in what it writes, and the values your filters compare them with, decrypting what it reads.

MongoFlow does the encrypting itself, with the driver's explicit encryption, so it works on MongoDB Community: MongoDB's
automatic encryption needs the crypt_shared library, which comes with Enterprise and Atlas.

[The samples](../samples/MongoFlow.Samples) show it end to end, in their twelfth step: KYC records an earlier version
stored in plain text, rebuilt encrypted at startup, then queried by their encrypted members.

## Installing

```bash
dotnet add package MongoFlow.Encryption --prerelease
```

It brings the driver's `MongoDB.Driver.Encryption` package, with libmongocrypt for Linux, macOS and Windows. MongoDB 8.0 or
later, as a replica set, as MongoFlow needs anyway.

## Declaring what's encrypted

```csharp
services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase("crm")
    .UseQueryableEncryption(qe => qe
        .KeyVault("encryption", "__keyVault")
        .Kms(new LocalKms(masterKey)))
    .Collection(x => x.Customers, customers => customers
        .Encrypt(x => x.Ssn, EncryptedQuery.Equality)
        .Encrypt(x => x.Salary, EncryptedQuery.Range(0, 1_000_000))
        .Encrypt(x => x.Notes)
        .Encrypt(x => x.Address!.Street)));
```

- **The key vault** is the collection the data keys are kept in, through the client the vault was given: one per
  deployment, usually, shared by every encrypted collection.
- **The KMS** protects the data keys: it holds, or reaches, the master key they're encrypted under, which never goes
  into MongoDB. See [Choosing a KMS](#choosing-a-kms).
- **Each encrypted member** is declared on its collection, nested ones too. Its BSON type comes from its serializer;
  give it to `Encrypt` as `bsonType` where the serializer doesn't tell, such as an `object` member.

What a member allows is fixed when its collection is created: MongoDB can't change an encrypted collection's fields.

| Declared with | Queries | Types |
|---|---|---|
| `EncryptedQuery.Equality` | `==`, `!=`, `Contains` on a list of values, `== null` | Strings, integers, dates, ids and binary |
| `EncryptedQuery.Range(min, max)` | `<`, `<=`, `>`, `>=`, `==`, `!=`, `Contains`, `== null` | `int`, `long`, `DateTime`, `double` and `decimal` with a `precision` |
| nothing | `== null` | Anything but `null` |

`EncryptedQuery.EqualityWith(contention)` and `Range`'s `contention` set the contention factor: higher guards values
many documents share better, and makes their queries slower. `Range` also takes `sparsity` and `trimFactor`, which trade
the index's size against query speed. A range's bounds are of the member's type; a value outside them can't be written.

## Choosing a KMS

Your fields are encrypted with data keys, one per field, which the migrator creates and keeps in the key vault. The data
keys are encrypted in turn with a master key, which the KMS holds or reaches. Whoever has the master key and the
database can read the data; losing the master key loses it.

```csharp
// A 96-byte key you hold, kept as a secret: generate it once with
// Convert.ToBase64String(RandomNumberGenerator.GetBytes(96)).
qe.Kms(LocalKms.FromBase64(configuration["Encryption:MasterKey"]!));

// AWS KMS, with the credentials of the environment: an EC2 or ECS role, or environment variables.
qe.Kms(AwsKms.FromEnvironment(new AwsMasterKey("eu-central-1", "arn:aws:kms:eu-central-1:111122223333:key/...")));

// Azure Key Vault, with the managed identity of the Azure host, or an app registration's secret.
qe.Kms(AzureKms.FromManagedIdentity(new AzureMasterKey("my-vault.vault.azure.net", "mongoflow")));
qe.Kms(new AzureKms(new AzureMasterKey("my-vault.vault.azure.net", "mongoflow"), tenantId, clientId, clientSecret));

// Google Cloud KMS, with the host's service account, or a service account's key.
qe.Kms(GcpKms.FromEnvironment(new GcpMasterKey("my-project", "europe-west1", "my-ring", "mongoflow")));

// A KMIP server, such as HashiCorp Vault, authenticated with a client certificate.
qe.Kms(new KmipKms("kmip.example.com:5696", new SslSettings { ClientCertificates = [certificate] }));
```

With a cloud KMS, the master key never leaves it: the app only has credentials allowed to use it, which can be audited and
revoked. Each provider takes `Tls`, for a certificate authority of your own. Every instance of the app needs the same
KMS. MongoDB Community works with all of them.

## Setting up the collections

An encrypted collection has to be created encrypted, with a data key for each encrypted field.
`IVaultMigrator.MigrateAllAsync` does it, before the vault's own migrations, so run it at startup before the vault is
used, as you would to migrate:

```csharp
await app.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
```

For each encrypted collection it finds:

- **none:** it creates the collection encrypted, and its data keys, with the [settings](configuration.md#collection-settings)
  it declares, such as a validator or a collation;
- **one encrypted as declared:** nothing to do;
- **one encrypted otherwise:** it fails, since MongoDB can't change an encrypted collection. Copy its documents into a new
  collection, under another name, encrypted as declared;
- **one that isn't encrypted,** with documents or without: it rebuilds it encrypted.

### Encrypting a collection that holds documents

MongoDB can't encrypt a collection in place, so a rebuild:

1. renames the collection to `{name}.encrypting`;
2. creates the encrypted collection under its name, with its declared settings but its validator, which documents
   stored before it may not pass, and which is added once they're copied;
3. copies the documents into it, in `_id` order and in batches, encrypting the declared members, and logs its progress;
4. copies the indexes that don't cover an encrypted member; the others, such as a unique index on `Ssn`, are left
   behind with a warning, as the server can't index encrypted values. The indexes it [declares](configuration.md#indexes)
   are set up after the migrations, as any collection's are, and an index declared on an encrypted member, or with a
   partial filter comparing one, fails at startup;
5. renames `{name}.encrypting` to `{name}.unencrypted`, which marks it done. Search indexes follow it there, with a
   warning, since they keep its documents indexed in plain text.

Stop the application while it runs: nothing may write to the collection meanwhile. A rebuild that's interrupted goes on
from where it stopped the next time the migrator runs.

`{name}.unencrypted` keeps the documents as they were, in plain text. MongoFlow never drops it: check the encrypted
collection, then drop it, in a later migration for instance:

```csharp
public sealed class DropUnencryptedCustomers : IVaultMigration<CustomerVault>
{
    public SemVersion Version { get; } = new(2, 1, 0);

    public string Description => "Drop the plain copy left by encrypting customers";

    public bool UseTransaction => false;

    public Task UpAsync(MigrationContext<CustomerVault> context, CancellationToken cancellationToken) =>
        context.Database.DropCollectionAsync("Customers.unencrypted", cancellationToken);

    public Task DownAsync(MigrationContext<CustomerVault> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

## Reading

Reads return documents decrypted. Filters MongoFlow renders have the values they compare encrypted members with
encrypted:

- `FindAsync`, `GetByKeyAsync` and the reads by filter, such as `FirstOrDefaultAsync(filter)`;
- `AggregateAsync`'s leading `$match`, from the query filters;
- the collection's query filters;
- in `QueryAsync`, each `Where` on the collection's documents, before any projection or join, and the predicate of
  `First`, `Any`, `Count` and the like.

```csharp
var customer = await vault.Customers.FirstOrDefaultAsync(x => x.Ssn == ssn);

var query = await vault.Customers.QueryAsync();
var wellPaid = await query.Where(x => x.Salary >= 5_000).OrderBy(x => x.Name).ToListAsync();
```

An encrypted member that is null is stored as absent, as MongoDB can't encrypt null: `x.Notes == null` finds documents
without it.

What MongoFlow can't encrypt throws `NotSupportedException`, rather than comparing plain values the server would match
nothing with:

- sorting or grouping by an encrypted member, or joining on one;
- comparing one inside a projection, or filtering after projecting one;
- an operator its query type doesn't allow, such as `>` on an equality member, a regular expression or `StartsWith`, or
  any comparison of a member encrypted without queries;
- a join on a collection whose query filters compare its encrypted members.

Stages appended to `AggregateAsync`'s pipeline, and a sort or a filter added to `FindAsync`'s result, aren't checked.
`MongoCollection` bypasses MongoFlow: its filters aren't encrypted, and the server refuses plain values written to an
encrypted field.

## Writing

Inserts and replaces store the encrypted members encrypted. Updates may `$set` and `$unset` them: a member set to null is
unset, and one inside a document set whole is encrypted inside it. Anything else that reaches an encrypted member, such
as `$inc` or `$push`, throws `NotSupportedException` before anything is written, and so does an update with a pipeline,
which the server refuses for an encrypted collection.

```csharp
vault.Customers.UpdateByKey(id, Builders<Customer>.Update.Set(x => x.Ssn, newSsn));
await vault.SaveAsync();
```

- **One write at a time.** The driver doesn't send client bulk writes through a client that encrypts, so a vault that
  encrypts saves with a command per write, in queue order, in its transaction: inserts into one collection that follow
  each other go in one. The server changes encrypted documents only one at a time, so `UpdateMany`, `DeleteMany`, and
  soft delete's update of a `DeleteMany`, find the matching documents in the save's transaction and write each in turn.
  They cost a round trip per document.
- **Change tracking** sets an encrypted member whole when it changes, as the server refuses an update inside one.
  Snapshots are kept in memory in plain text, as the document is, and cleared once given back.
- **The concurrency token**, soft delete's flag and multi-tenancy's tenant can't be encrypted, nor can the `_id` or a
  custom key: MongoFlow compares them with null, increments them, or matches them in every write. The model fails to
  build if one is.
- **Documents must ignore extra elements** (`[BsonIgnoreExtraElements]`), with no extra-elements member: the server adds
  `__safeContent__` to each encrypted document, which reading back would fail on, and which a replace sending it back
  would be refused for.
- `RenderFilter()`, `RenderUpdate()` and `Original`, which interceptors read, are in plain text: they're what the save
  sends before it's encrypted.

## Clients and transactions

A vault that encrypts reads and writes through a client made from the one it was given, which adds what the server needs
to write and query encrypted values, and decrypts what it reads. Vaults with the same key vault and KMS over one client
share it, so they save in one transaction.

A vault that doesn't encrypt uses the client it was given, which can't join a transaction on another client. To save it in
the same transaction as an encrypted one, give it the same `UseQueryableEncryption` settings, in a default configuration
for instance: it encrypts nothing, and shares the client.

The client you give a vault that encrypts must not have `AutoEncryptionOptions` of its own.

## MongoDB Search

MongoDB Search can't index a collection with Queryable Encryption, so a search index declared on a collection with
encrypted members fails the model, and searching one throws `NotSupportedException`. A vault that encrypts searches its
other collections through the client that encrypts, and gets their documents back decrypted: keep what's searched in a
collection of its own. A rebuild warns when the collection had search indexes, which follow its documents to
`{name}.unencrypted` and keep them indexed in plain text. See [MongoDB Search](search.md#queryable-encryption).

## Logs

`MongoFlow.Encryption` logs what the migrator does: a collection created or found as declared, a rebuild's start,
progress and end at `Information`, and at `Warning` an index a rebuild left behind, search indexes that followed the
unencrypted copy, and an unencrypted copy still there. `MongoFlowLogEvents.Encryption` names the events.
