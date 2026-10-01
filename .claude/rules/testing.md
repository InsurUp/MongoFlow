---
description: Rules for writing and modifying MongoFlow's tests
globs: ["**/*Tests*/**", "**/*Test*.cs", "**/tests/**"]
---

# Test Authoring Rules

The same standard as InsurUp core (`docs/standards/08-testing.md`), Prest and Volucer, with what a MongoDB library
adds: two lanes, a real server for anything that saves, and snapshots of what was stored.

## Projects and lanes

- `tests/MongoFlow.Tests`: the unit lane. No server: vaults resolve against `Offline.Client`, a client of a server
  that isn't there, which is enough to configure vaults, queue writes and build queries (`QueryAsync().ToString()`
  shows the MQL). It sees internals (`InternalsVisibleTo`) for pure helpers such as `FeatureSet`, `KeyModel` and the
  expression helpers.
- `tests/MongoFlow.IntegrationTests`: the integration lane, through the public API only. A Testcontainers MongoDB 8.2
  single-node replica set (`MongoFixture`), shared by the whole run: saves need MongoDB 8.0+ (client bulk writes) and
  a replica set (transactions). Its tests are in the `Integration` category (`AssemblyInfo.cs`) and need Docker.
- Both target `net10.0` and `net11.0`, like the library.

## Framework and tooling

- **TUnit** on Microsoft.Testing.Platform (`global.json`): `[Test]`, `[Arguments]`, `await Assert.That()`. No xUnit,
  NUnit or MSTest attributes, no `Microsoft.NET.Test.Sdk`, no coverlet.
- **Verify** (`Verify.TUnit`) for snapshots, **TUnit.Mocks** for mocks, **Testcontainers.MongoDb** for the server,
  **FakeTimeProvider** for time. No FluentAssertions, Shouldly, Moq or NSubstitute.
- Package versions live in `Directory.Packages.props`; `tests/Directory.Build.props` holds what every test project
  shares (target frameworks, warnings as errors, the Verify exemption). Don't re-reference a package the library brings.
- **Setup:** constructors with `readonly` fields for sync setup; `[Before(Test)]` only when it must be async. No `null!`
  field initializers.

## Naming

- Test method: `{MethodName}_{Scenario}_{ExpectedResult}`, three parts: `SaveAsync_AWriteFails_WritesNothing`. A test
  covering several members names them together: `DeleteByKeyAndDeleteMany_SoftDeletable_MarkTheStoredDocuments`.
- Test class: `{ClassName}Tests`. Partial files: `{ClassName}Tests.{MethodGroup}.cs`; doubles used by one class go in
  `{ClassName}Tests.Doubles.cs` as nested types, shared ones in `Fixtures/`, one type per file.

## Structure

- AAA with comments: `// Arrange`, `// Act`, `// Assert`; `// Act & Assert` when the act is the assertion. Say why with
  an em dash: `// Arrange — the second insert repeats a stored key.`
- No `#region`; split a file before it passes 500 lines.
- Asserted values are explicit in the test body. A value both arranged and asserted is a local `const` (PascalCase).
- Tests at the top of a file, helpers at the bottom. Comments in a test body explain why, never what.

## Assertions

- Await every assertion; actual first. One `Assert.That` per line, for a single value.
- **2+ properties of one object → `Verify(...)`**, often as an anonymous object: `Verify(new { result, Stored = ... })`.
- `ThrowsExactly<T>()` pins the type. When the message is part of the contract, snapshot the exception:
  `await Throws(() => ...).IgnoreStackTrace();`, `ThrowsTask`, `ThrowsValueTask`.
- Collections: `IsEquivalentTo(expected, CollectionOrdering.Matching)` when order matters.

## Snapshots (Verify)

- `ModuleInitializer.cs` puts snapshots under `snapshots/`; `VerifyChecksTests.cs` checks the repository settings
  Verify needs (`.editorconfig`, `.gitattributes`, `.gitignore`). Commit `*.verified.txt`; `*.received.*` is ignored.
- `BsonValueConverter` writes BSON as relaxed extended JSON, so `Verify(await host.StoredAsync("Orders"))` shows what
  the server holds, element names included.
- Snapshots keep default values (`Matched: 0`, `false`, `Kind: Insert`) but not nulls. Empty collections are left out
  unless the call says `.DontIgnoreEmptyCollections()`; say it wherever "nothing was written" is the point.
- Fixed values only: fixed ids, `ObjectId.Parse(...)`, `Guid.Parse(...)`, `FakeTimeProvider` dates. A database name is
  generated per test, so snapshot documents, never namespaces.
- One verified file serves both target frameworks.

## Integration tests

- Take the server by property injection, not the constructor, so Verify doesn't put it in snapshot names:
  `[ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)] public required MongoFixture Mongo { get; init; }`.
- Isolation is a database per test: `Mongo.Host<TVault>(configure, services)` registers the vault on a new database
  with its own service provider and scope. For two vaults in one app, take `Mongo.NewDatabase()` and register both.
- Tests run in parallel. The one shared resource is the server's `failCommand` failpoint (`Mongo.FailNextAsync`), which
  a test setting it replaces for everyone: tests that use it are `[NotInParallel("failCommand")]`, and their client is
  named so the failpoint singles it out (`Mongo.CreateClient(name)`).
- Assert what was stored by reading it back (`StoredAsync`), not by trusting the save's result alone.

## Mocks

- TUnit.Mocks, for interfaces only (`IMongoClient`, `IMongoDatabase`, `IClientSessionHandle`, `IVaultConfiguration<T>`).
  Never mock a concrete class: interceptors are hand-written doubles with a `<summary>` saying what they stand in for.
- Setups and verifications take the arguments that matter; `Any()` is for those that don't, such as a cancellation
  token, with a comment when it isn't obvious.

## Data-driven tests

- `[Arguments]` by default; `[MethodDataSource]` returning `IEnumerable<Func<(string, ...)>>` for delegates, with a
  readable first element naming the case; `[MatrixDataSource]` for a cartesian product.

## Parallelism and flakiness

- Hermetic tests, no static mutable state. `[NotInParallel("Key")]` only with a comment naming the shared resource.
- `[Retry]` is banned, and so are sleeps.

## Documentation

- Every test class has a `<summary>` stating the guarantee it protects. Test methods have no XML docs.

## Coverage

- Near 100% line and branch, as a union of both lanes. TUnit collects it with `--coverage`, on Apple silicon too.
- `coverage.settings.xml` leaves out tests, samples and generated code, each exclusion with its reason. Never exclude
  to make the number look better.
- **Dead code rule:** code no user can reach is removed, not tested. What stays uncovered, and why:
  - `AsyncQueryFilter.Resolve`: the synchronous path only runs when a collection has no asynchronous filter;
  - two guards in `VaultModelBuilder.IsSameProperty` for selectors a lambda can't express;
  - the rethrow branches the compiler generates for `throw;` in a catch or finally that awaits (`SavePipeline`,
    `VaultTransaction`): they handle thrown objects that aren't exceptions.
- Every bug fix comes with a regression test that fails before the fix.

### Commands

With SDK 11 RC, a relative `--project` path to a multi-target project is resolved against the project's own folder
and fails; pass `-f`, an absolute path, or run `dotnet test` from the project's folder.

```bash
dotnet test --solution MongoFlow.sln                              # both lanes, both frameworks (needs Docker)
dotnet test --project tests/MongoFlow.Tests -f net10.0            # unit lane only, no Docker
dotnet test --project tests/MongoFlow.Tests -f net10.0 -- --treenode-filter "/*/*/VaultBuilderTests/*"

# Coverage: each lane, then the union. reportgenerator can't union branches from two Cobertura files,
# dotnet-coverage merge can.
for p in MongoFlow.Tests MongoFlow.IntegrationTests; do
  dotnet test --project tests/$p -f net10.0 -- --coverage --coverage-settings coverage.settings.xml \
    --coverage-output-format cobertura --coverage-output $p.cobertura.xml
done
dotnet-coverage merge --output TestResults/coverage.cobertura.xml --output-format cobertura \
  TestResults/MongoFlow.Tests.cobertura.xml TestResults/MongoFlow.IntegrationTests.cobertura.xml
reportgenerator -reports:TestResults/coverage.cobertura.xml -targetdir:TestResults/CoverageReport \
  -reporttypes:"TextSummary;Html"
```

## Don'ts

- **Don't** use `--filter` (VSTest syntax, silently ignored); use `--treenode-filter`
- **Don't** use `--collect:"XPlat Code Coverage"`; TUnit uses `--coverage`
- **Don't** write 2+ `Assert.That` calls on one object's properties; `Verify` it
- **Don't** inject `MongoFixture` through the constructor
- **Don't** share a database between tests, or register class maps or conventions (they're global to the process)
- **Don't** mock concrete classes or MongoDB extension methods
