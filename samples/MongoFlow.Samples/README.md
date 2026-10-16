# MongoFlow samples

A small insurance platform written against MongoFlow, MongoFlow.Identity and MongoFlow.Encryption, to show how the API
reads in a real app.
It runs: it migrates every vault, then walks through the platform's services as its users' requests would, and logs
what each step shows.

## Running

From the repository root, with Docker running:

```bash
dotnet run --project samples/MongoFlow.Samples
```

It starts MongoDB's Atlas Local image for the run, MongoDB 8.3 as a replica set, which saves need, with MongoDB Search. To
use a server of your own instead, MongoDB 8.0 or later as a replica set with MongoDB Search (Atlas, or Community with
mongot), name it; each run then writes to databases of its own, named `samples_{time}_...`, or to those `Database:Prefix`
names:

```bash
dotnet run --project samples/MongoFlow.Samples -- --ConnectionStrings:Mongo="mongodb://localhost:27017/?replicaSet=rs0"
```

CI runs it after the tests, so a change that breaks a sample fails the build.

The KYC records are encrypted under a local master key, `Encryption:MasterKey` in base64. Without one, each run makes
its own, which is enough for databases of its own; a server of your own with a fixed `Database:Prefix` needs the same key
every run, or the records encrypted before can't be read:

```bash
dotnet run --project samples/MongoFlow.Samples -- --Encryption:MasterKey="$(openssl rand -base64 96 | tr -d '\n')"
```

### Logs

MongoFlow logs under `MongoFlow.*` categories, with every event ID in `MongoFlowLogEvents`; `appsettings.json` shows
migrations and index warnings. To see each save, the transaction it runs in and what it wrote:

```bash
dotnet run --project samples/MongoFlow.Samples -- --Logging:LogLevel:MongoFlow.Save=Debug
```

### Traces and metrics

`Program.cs` adds MongoFlow's activity source and meter to OpenTelemetry, with the driver's source, whose command spans
nest under MongoFlow's saves. To see them, start the Aspire dashboard and point the samples at it:

```bash
docker run --rm -d -p 18888:18888 -p 4317:18889 --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run --project samples/MongoFlow.Samples
```

Then open http://localhost:18888, with the login token from `docker logs aspire-dashboard`.

## The walkthrough

`Walkthrough/PlatformWalkthrough.cs` reads top to bottom. Each step runs in a DI scope of its own, signed in as the
platform's admin or an agent of one of two agencies (`SampleUsers`), as authentication would sign in a request.

| Step | What it shows | Where |
|---|---|---|
| 1. Seed | Inserts stamped with the tenant and timestamps; events stored by a transactional outbox; a strongly typed key; a composite key | `PolicyService.IssueAsync`, `OutboxInterceptor`, `AgencyService`, `CustomerService` |
| 2. Reads | Key lookups, LINQ, paging, find, projection and aggregation, each limited by tenant, soft delete, permissions and modules | `PolicyService` |
| 3. Change tracking | A policy changed where it was read; the save writes what changed | `PolicyVault.Configure`, `PolicyService.CancelAsync` |
| 4. Concurrency | Two requests change one policy: the second save throws `ConcurrencyException`, and the request reads again | `PolicyVault.Configure` |
| 5. Transactions | A claim filed across two vaults; one over the customer's limit rolled back after its first save. Then the open claims joined with their policies, through the policies' query filters | `ClaimService.FileAsync`, `ClaimService.OpenWithPoliciesAsync` |
| 6. Write conditions | A claim decided only while it's open, checked by the server in the write | `ClaimDecisionGuard`, `ClaimService.DecideAsync` |
| 7. Set-based writes | An update of every ended policy, a soft delete by key, and a count with features switched off | `PolicyService` |
| 8. Parallel reads | Policies read from parallel tasks on one vault, renewed, and saved once | `RenewalService` |
| 9. Identity | ASP.NET Core Identity's managers on MongoFlow.Identity: an account deleted, which soft delete keeps, and restored | `AccountService`, `PlatformUserVault` |
| 10. Audit | An interceptor writing to another vault, in the transaction of each change | `AuditTrailInterceptor`, `AuditReader` |
| 11. Migrations | The policy vault reverted down to a version, then migrated up again | `Migrations/` |
| 12. Encryption | KYC records an earlier version stored in plain text, rebuilt encrypted by the migrator at startup, its index kept; found by an encrypted national id and an encrypted birth date range, noted, and added; what the server holds, and the plain copy left to drop | `KycVault`, `KycService`, `LegacyKycRecords` |
| 13. Search | Customers searched by the start of a name and counted, each agent finding the agency's alone, filtered in a search index the migrator created; in an app that encrypts its KYC records too | `CustomerVaultConfiguration`, `CustomerService.SearchByNameAsync` |

## Layout

| Folder | What it shows |
|---|---|
| `Domain/` | Tenant-owned, soft-deletable documents, plus permission and module attributes |
| `Vaults/` | Vaults that configure themselves: a custom string key with a concurrency token and change tracking, soft delete by timestamp, a custom struct key, a composite key, Identity's vault, and a vault with encrypted members |
| `Configuration/` | Two defaults, two features driven by attributes with async filters, a naming convention, and a per-vault configuration that reads options |
| `Interceptors/` | Timestamps (typed, per collection, adding to update definitions), an audit trail (writes to another vault in the same transaction, switchable as a feature), a transactional outbox, and a write condition |
| `Migrations/` | Indexes created through the vault's collections, a data fix through the vault, a schema change with the driver, and migrations outside a transaction |
| `Services/` | The app's services, which the walkthrough calls |
| `Infrastructure/` | The server for the run, with MongoDB Search, the request's user, a serializer for the strongly typed id, and the KYC records an earlier version stored in plain text |
| `Program.cs` | Registration (defaults, per-vault databases, a second client, skipping defaults, Identity, Queryable Encryption with a local KMS), logging, telemetry and migrating at startup, which sets up the search index too |

## Gaps

None left in the API as far as these samples reach.
When a new scenario needs something the API can't express, write it the way it should read inside `#if MISSING_API`;
building with `-p:DefineConstants=MISSING_API` then lists what's missing.

## Friction that remains

- **Built-in shortcuts need a typed lambda parameter,** `UseSoftDelete((ISoftDeletable x) => x.IsDeleted)`. C# can't
  infer one type argument and accept another explicitly, so `UseSoftDelete<ISoftDeletable>(...)` isn't possible.
- **Database prefix:** a per-environment prefix is repeated in every registration, because there's deliberately no
  name hook.
- **Strongly typed ids** like `AgencyId` need serializers registered with the driver (`AgencyIdSerializer`), and so do
  `Guid`s, whose representation the driver asks for; MongoFlow leaves serialization to the driver.
- **A vault that encrypts uses a client of its own,** so the audit interceptor, which saves the audit vault in each
  change's transaction, can't join its saves: `KycVault` skips the platform's defaults and limits reads to the agency
  itself. Giving the audit and outbox vaults the same encryption settings would let them join, at the cost of their
  saves going a command per write.
