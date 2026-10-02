# Logging, tracing and metrics

## Logging

MongoFlow logs through the `ILoggerFactory` in DI, if there is one. `MongoFlowLogEvents` names every category and event
ID; refer to them by constant, not by number.

| Category | Logs |
|---|---|
| `MongoFlow.Model` | A vault's model built, at `Debug` |
| `MongoFlow.Indexes` | Indexes a vault relies on and lacks, at `Warning`; the check itself at `Debug` |
| `MongoFlow.Save` | Each save: starting, how many tracked documents changed, and how it ended, at `Debug`; each write at `Trace`; a concurrency conflict or a write to another tenant, at `Debug`; an interceptor's `FailedAsync` or `CommittedAsync` throwing, at `Error` |
| `MongoFlow.Transaction` | Transactions begun with `BeginAsync`: begun, committed, rolled back or doomed, at `Debug`; one a save opens for itself at `Trace` |
| `MongoFlow.Query` | Each read, with the query filters it runs with, at `Trace` |
| `MongoFlow.Migrations` | Migrations applied and reverted, at `Information`; a history recording a version twice, at `Warning`; a failure at `Error` |

```json
{
  "Logging": {
    "LogLevel": {
      "MongoFlow": "Information",
      "MongoFlow.Save": "Debug"
    }
  }
}
```

A failed save logs at `Debug`, since its exception reaches the caller, who decides how loud it is.

### Missing indexes

MongoFlow doesn't create indexes, but it checks them, once per vault, in the background when its model is built, and
warns about:

- a key other than `_id` that no unique index covers, so lookups and writes by key scan the collection;
- a key whose index isn't unique, so a key can match more than one document;
- a field a feature filters every read on, such as the soft-delete flag or the tenant, that no index includes.

A collection with no indexes at all isn't checked, since it may not exist yet. Create indexes in a
[migration](migrations.md).

## Tracing

MongoFlow traces through `System.Diagnostics`, so with OpenTelemetry it's one more source. Add the driver's too: its spans
for the commands MongoFlow sends nest under MongoFlow's.

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(MongoFlowTelemetry.ActivitySourceName)
        .AddSource(MongoTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(MongoFlowTelemetry.MeterName));
```

| Span | For | Tags |
|---|---|---|
| `MongoFlow.Save` | A save with writes queued | The vault; whether it ran in a transaction of its own or joined one; the writes it sent; what it inserted, matched, modified and deleted |
| `MongoFlow.Migrate` | One vault migrated | The vault; its version before, and the target |
| `MongoFlow.Migration` | One migration applied or reverted | The vault; the migration's version and name; up or down |

A span that fails has an error status, `error.type` and an exception event. `MongoFlowTelemetry.Activities` and
`MongoFlowTelemetry.Tags` name them all.

The driver's spans for a save's bulk write and commit nest under its `MongoFlow.Save` span. In a transaction begun with
`BeginAsync`, the driver's span for the transaction starts under the code that began it. Reads have no span of their
own: a query runs after `QueryAsync` returns, and the driver traces it.

Nothing is allocated when nothing listens, and tags are set only on spans that are recorded.

## Metrics

| Instrument | Type | Measures | Tags |
|---|---|---|---|
| `mongoflow.save.duration` | Histogram, seconds | Each save with writes, comparing tracked documents included | The vault; own or joined transaction; `error.type` when it failed |
| `mongoflow.save.operations` | Counter | Writes, counted when their transaction commits, so writes rolled back never count | The vault; the kind: insert, replace, update or delete |
| `mongoflow.transaction.duration` | Histogram, seconds | How long transactions begun with `BeginAsync` stay open; a save's own is the save's | The outcome: committed, rolled back, or commit failed; `error.type` |

`MongoFlowTelemetry.Instruments` names them. The meter comes from the `IMeterFactory` in DI when there is one, which
hosts register, and is the same for every vault of a provider.

To see both in development, run the Aspire dashboard and set `OTEL_EXPORTER_OTLP_ENDPOINT`; the
[samples](https://github.com/InsurUp/MongoFlow/tree/main/samples/MongoFlow.Samples) do it with `UseOtlpExporter()`.
