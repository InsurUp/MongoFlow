---
description: Rules for writing and modifying MongoFlow's benchmarks
globs: ["**/benchmarks/**", "**/*Benchmarks*.cs"]
---

# Benchmark Authoring Rules

The same rules as Prest and Volucer: BenchmarkDotNet, a competitor in every class, allocations measured. What MongoFlow
adds: what it costs over the driver doing the same work, with and without a server.

## Framework

- **BenchmarkDotNet**: `[Benchmark]`, `[GlobalSetup]`, `[Params]`. Its version comes from `Directory.Packages.props`.
- `[MemoryDiagnoser]` on every class: what MongoFlow allocates per request matters as much as its time.
- The project sees MongoFlow's internals (`InternalsVisibleTo`), for hot paths a user can't call alone, such as
  `KeyModel.Match`. Everything else goes through the public API.

## Architecture

- **No base classes.** Every benchmark class is self-contained; shared pieces are composed from `Shared/`:
  - `Vaults`: service providers with a `ShopVault`, without features or with soft delete, the concurrency token and
    multi-tenancy, and `Vaults.Offline`, a client of a server that isn't there;
  - `BenchmarkDatabase`: a database of its own for one benchmark case on the benchmark server, dropped at the end;
  - `ClientBulkWrites`: the driver doing what a save does, one ordered, verbose client bulk write in a transaction;
  - `BenchmarkServer`: the server, named in `MONGOFLOW_BENCHMARKS_MONGO`, or a container `Program` starts for the run;
  - parameter sets reused across classes, as `ParamsAttribute` subclasses (`DocumentCountParamsAttribute`).
- One type per file. Directories by what's measured: `Startup/`, `Reads/`, `Writes/`, `Saves/`, `Shared/`.

## Competitors

- Every class has the driver doing the same work as its baseline (`Baseline = true`): the same client bulk write,
  `AsQueryable` with the filter written in, a repository over the driver's collections, the driver's own filters.
- Where the driver has nothing to compare, such as building a vault's model, the plainest MongoFlow case is the
  baseline, so the table shows what each feature adds.
- Measure what a request does: resolve the vault from a new scope inside the benchmark, as each request would.

## Naming

- Class: `{WhatIsMeasured}Benchmarks`, file named like the class.
- Method: `Driver`, `MongoFlow`, then the variant: `MongoFlowWithFeatures`, `MongoFlowTokenChecked`.
- `Description`: `"Driver: client bulk write"`, `"MongoFlow: save with features"`: who, then what.
- Every class has a `<summary>` saying what is measured, against what, and what isn't measured.

## Benchmarks that save

- They need MongoDB 8.0 or later as a replica set. `Program` starts a `mongo:8.2` container for the run and names it in
  `MONGOFLOW_BENCHMARKS_MONGO`, which the benchmark processes inherit; set the variable to use a server of your own.
- Each case gets its own database (`BenchmarkDatabase`), created with its collection so no save creates one inside a
  transaction.
- Run the first save in `[GlobalSetup]`: it asks the server whether it supports client bulk writes, once per client.
- Keep the data the same through a run: write what saves can repeat (updates that increment), or restore it in
  `[IterationCleanup]` (inserts emptied, soft deletes restored). An iteration cleanup makes BenchmarkDotNet run one
  invocation per iteration, which suits saves of a millisecond or more.
- Times against a server vary with the machine and the server; compare runs from one machine, and read allocations
  as the steadier signal.

## Parameters

- Inline `[Params]` for a set only one class uses; a `ParamsAttribute` subclass in `Shared/` for one several use.
- `[ParamsAllValues]` over an enum for kinds of input (`FilterKind`, `KeyShape`), each value documented.
- **Never change parameter values to make a run faster.** Use `--filter`, `--param:` or `--job`.

## Code style

- MongoFlow's own: explicit `private`, one parameter per line in multi-parameter signatures, file-scoped namespaces.
- Attributes on their own line. A benchmark class isn't sealed: BenchmarkDotNet derives from it.

## Commands

```bash
# Everything; the container starts unless MONGOFLOW_BENCHMARKS_MONGO names a server
dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*'

# One class, short (3 iterations) or dry (1, to check it runs)
dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*InsertBenchmarks*' --job short
dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*' --job dry

# One parameter value
dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*SoftDelete*' --param:Count=10000

# List
dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --list flat
```

Reports land in `benchmarks/artifacts/results/` (committed); logs elsewhere in `benchmarks/artifacts/` aren't. CI can run
every benchmark once (`--job dry`) when started by hand with `benchmarks` checked, to check they still run; timings from a
shared runner aren't worth keeping.

## Don'ts

- **Don't** use base classes for benchmarks
- **Don't** leave out the driver baseline when the driver can do the same work
- **Don't** let saved data grow or drift through a run
- **Don't** pin package versions in the benchmark project
