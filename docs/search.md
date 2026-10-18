# MongoDB Search

[MongoDB Search](https://www.mongodb.com/docs/atlas/atlas-search/) searches a collection by relevance: full text,
autocomplete, facets, and with Vector Search, the nearest embeddings. It runs on Atlas, and on MongoDB Community 8.2 or
later beside its search process, mongot.

MongoFlow brings it to a vault: you declare a collection's search indexes, the migrator creates them, keeps them as
declared and waits until they're queryable, and searches filter with the collection's query filters in the index, so a
search finds only what a read would.

```csharp
services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase("crm")
    .UseSoftDelete((Customer x) => x.IsDeleted)
    .UseMultiTenancy((Customer x) => x.AgencyId, services => services.GetRequiredService<IAgency>().Id)
    .Collection(x => x.Customers, customers => customers
        .SearchIndex("default", index => index
            .Analyzer("lucene.turkish")
            .Map(x => x.Name, SearchField.String(), SearchField.Autocomplete()))
        .VectorSearchIndex("vectors", index => index
            .Vector(x => x.Embedding, dimensions: 1536, VectorSimilarity.Cosine))));

await app.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

var search = await vault.Customers.SearchAsync(Builders<Customer>.Search.Text(x => x.Name, "ada"));
var adas = await search.Limit(20).ToListAsync();
```

The search above finds the current agency's customers named Ada who aren't deleted: the index maps the agency and the
soft-delete flag, which MongoFlow added to it, and the search filters on them.

## Declaring indexes

`SearchIndex(name, ...)` declares a search index, `VectorSearchIndex(name, ...)` a vector search index. A search uses the
index named `default` unless it names another. Declared again, from another configuration for instance, an index is
configured further.

```csharp
customers.SearchIndex("default", index => index
    .Analyzer("lucene.turkish")                 // what text is indexed with, unless a field says otherwise
    .SearchAnalyzer("lucene.standard")          // what queries are analyzed with
    .Dynamic()                                  // index the fields not mapped, by their type
    .Map(x => x.Name, SearchField.String(), SearchField.Autocomplete(), SearchField.Token())
    .Map(x => x.Address!.City, SearchField.Token())
    .Map("Tags", SearchField.Token()));
```

| `SearchField` | Indexes | For |
|---|---|---|
| `String(analyzer, searchAnalyzer)` | Text, analyzed | `text`, `phrase` |
| `Autocomplete(minGrams, maxGrams, tokenization, foldDiacritics)` | Text, cut into prefixes | `autocomplete` |
| `Token(lowercase)` | Text as one exact token | `equals`, `in`, `range`, sorting, string facets |
| `Number()`, `Date()`, `Boolean()`, `ObjectId()`, `Uuid()` | Values of the type | `equals`, `in`, `range`, `near` |
| `Raw(definition)` | A mapping written as MongoDB takes it, such as `{ type: "geo" }` | |

Members are mapped by their element names, `[BsonElement]` included; a dotted path, such as `Address.City`, maps the
documents that hold it too. `Definition(BsonDocument)` starts from a definition written by hand, kept in JSON for instance,
and what the other calls declare is added to it.

A vector search index indexes vectors, and the fields its filter compares:

```csharp
customers.VectorSearchIndex("vectors", index => index
    .Vector(x => x.Embedding, dimensions: 1536, VectorSimilarity.Cosine, VectorQuantization.Scalar)
    .Filter(x => x.Segment));
```

### What MongoFlow maps

The fields features filter every read on, such as soft delete's flag and multi-tenancy's tenant, are mapped in each
index of the collection, as their values compare: a string as a `token`, a `bool` as a `boolean`, a number as a `number`,
a `DateTime` as a `date`, an `ObjectId` as an `objectId` and a standard `Guid` as a `uuid`; in a vector index, as filter
fields. A field mapped otherwise already is left as mapped, and one whose values a search can't compare exactly, such as
a document, is left out.

## Searching

```csharp
// $search, then whatever the driver chains on.
var search = await vault.Customers.SearchAsync(
    Builders<Customer>.Search.Autocomplete(x => x.Name, "ad"),
    new SearchOptions<Customer> { Sort = Builders<Customer>.Sort.Ascending(x => x.Name) });
var page = await search.Skip(20).Limit(20).ToListAsync();

// $searchMeta: what a search counts and its facets count, of what the query filters show.
var meta = await vault.Customers.SearchMetaAsync(
    Builders<Customer>.Search.Facet(
        Builders<Customer>.Search.Text(x => x.Name, "ada"),
        Builders<Customer>.SearchFacet.String("segments", x => x.Segment)),
    count: new SearchCountOptions { Type = SearchCountType.Total });

// $vectorSearch, by a member or a field definition.
var similar = await vault.Customers.VectorSearchAsync(x => x.Embedding, new QueryVector(embedding), limit: 10,
    new VectorSearchOptions<Customer> { IndexName = "vectors", NumberOfCandidates = 200 });
```

Each returns the driver's own type, as `AggregateAsync` does. Searches aren't tracked, and run outside the scope's
transaction, which the server doesn't search in. They see what's committed once mongot has replicated it, a moment after
it's written.

## Query filters in a search

A search stage has to come first, so the query filters can't run before it as a `$match`. MongoFlow translates them into
clauses the search filters with in the index: `filter` and `mustNot` of a compound around your operator, whose score is
your operator's, or for a vector search, its filter. What it can't translate runs as a `$match` right after the search
stage, so a search never shows what the query filters hide.

A part of the query filters is filtered in the index when:

- the index maps its field to compare exactly: a string as a `token` that doesn't lowercase; a number, date, boolean,
  ObjectId or UUID as its type; for a vector index, a filter field;
- it compares with `==`, `!=`, `<`, `<=`, `>`, `>=`, `Contains` on a list of values, `== null` and `!= null`, `!`, and
  `&&`, and `||` when every branch can be.

A regular expression, `$expr`, an element of an array matched as a whole, a decimal, and a field the index doesn't map,
such as a string mapped only as analyzed text, run after the search, and MongoFlow logs a warning the first time each
field does (`FilterFieldNotPreFiltered`). Map the field to filter on it in the index.

What runs after the search can't be counted by it, so:

- `SearchAsync` throws `NotSupportedException` when it counts (`CountOptions`), facets or returns stored source while part
  of the query filters runs after it;
- `SearchMetaAsync` throws when any part does, since nothing can follow `$searchMeta`;
- `VectorSearchAsync` can return fewer documents than its limit, as the limit applies before the `$match`; with stored
  source, it throws.

A per-query filter that returns `_ => false` matches nothing: searches return nothing, and `SearchMetaAsync` counts
nothing without asking the server.

## Pipelines of your own

`SearchFiltersAsync(indexName)` gives the query filters as a search applies them, for a pipeline built by hand:

```csharp
var filters = await vault.Customers.SearchFiltersAsync("default");

var pipeline = vault.Customers.MongoCollection.Aggregate()
    .Search(filters.Apply(Builders<Customer>.Search.Text(x => x.Name, "ada")));
if (filters.PostFilter is { } after)
{
    pipeline = pipeline.Match(new BsonDocumentFilterDefinition<Customer>(after));
}
```

`Filter` and `MustNot` are the clauses, `PostFilter` what's left for a `$match`, `CanCount` whether counts, facets and
stored source show only what the query filters do, and `MatchesNothing` whether they match nothing.

## Setting up indexes

`IVaultMigrator.MigrateAllAsync`, and `MigrateAsync`, set up a vault's declared indexes after its migrations, which may
create the collections or rename the fields they map:

- **none of the name:** the index is created, and its collection, if missing, with its [declared settings](configuration.md#collection-settings);
- **one defined as declared:** nothing to do. The definitions are compared as the server lists them, with its defaults,
  and without the index's own metadata, such as its name, which self-managed mongot lists with them;
- **one defined otherwise:** it's updated. Searches use it as it was until it's rebuilt;
- **one of the other type:** it fails, since a type can't be changed.

The migrator then waits until every index it created or updated, and any still building, is built and queryable, so
searches find what's stored once it returns; one that fails to build fails it. Each index is set up in parallel, and the
wait is bounded by the migrator's cancellation token. An index no longer declared isn't dropped: drop it in a migration.

Over a large collection, the wait can take minutes. To start serving sooner, the migrator can return without it:

```csharp
await migrator.MigrateAllAsync(new MigrateOptions { WaitForSearchIndexes = false });
```

It then returns once the indexes are created or updated: until it's built, a created index finds nothing, and an updated
one is searched as it was. One that fails to build no longer fails the migrator: `WaitUntilQueryableAsync`, below, waits
for one and throws if it failed.

Seed a collection before its index is built, in tests for instance: what's written afterwards is searchable a moment
later.

### Indexes managed by hand

For an index the vault doesn't declare, the helpers on `IMongoSearchIndexManager` do in a migration what the migrator
does for a declared one:

```csharp
public async Task UpAsync(MigrationContext<CustomerVault> context, CancellationToken cancellationToken)
{
    var indexes = context.Database.GetCollection<BsonDocument>("Customers").SearchIndexes;
    await indexes.CreateOrUpdateAsync("byHand", definition, SearchIndexType.Search, cancellationToken);
    await indexes.WaitUntilQueryableAsync("byHand", TimeSpan.FromMinutes(5), cancellationToken);
}
```

They aren't part of the migration's transaction: one that fails after creating an index doesn't remove it. The collection
must exist. A search of an index the vault doesn't declare reads its definition from the server the first time, and keeps
it, to learn which fields it maps.

## Queryable Encryption

MongoDB Search can't index a collection with Queryable Encryption: the server keeps encrypted values, which can't be
searched, and mongot would index whatever is in plain text. So:

- a search index declared on a collection with encrypted members fails the model, with a `VaultConfigurationException`;
- searching one throws `NotSupportedException`.

A vault that encrypts can search its other collections: it searches through the client that encrypts, as it reads, and
gets the documents back decrypted. Keep what's searched in a collection of its own, in plain text, and the encrypted
members in another.

Never map an encrypted field in an index you create by hand, nor compare one in a search's clauses: MongoFlow doesn't
encrypt search clauses. CVE-2026-9741, where a client's automatic encryption sent `$vectorSearch` filter values on
encrypted fields in plain text, concerns automatic encryption's query analysis, which MongoFlow's explicit encryption
doesn't use.

When the migrator [rebuilds a collection encrypted](encryption.md#encrypting-a-collection-that-holds-documents), its search
indexes follow its documents to `{name}.unencrypted`, so mongot keeps indexing them in plain text: it warns
(`SearchIndexesFollowCopy`). Drop the indexes, or the unencrypted copy, which drops them too.

## Logs and spans

`MongoFlow.Search` logs what the migrator does to each index: created or updated at `Information`, found as declared and
building at `Debug`, queryable at `Information`, failed at `Error`; and a field searches filter on after the search, at
`Warning`. `MongoFlow.Query` logs each search at `Trace`, with its index, its clauses and what runs after it. Each index set
up is traced as a `MongoFlow.SearchIndex` span under `MongoFlow.Migrate`. See [observability](observability.md).

## Running it locally

MongoDB's [Atlas Local image](https://hub.docker.com/r/mongodb/mongodb-atlas-local) runs mongod and mongot in one
container, as a single-node replica set, which saves need too:

```bash
docker run -d -p 27017:27017 mongodb/mongodb-atlas-local:8.3.4
```

Connect with `mongodb://localhost:27017/?directConnection=true`. MongoFlow's own search tests run against it.
