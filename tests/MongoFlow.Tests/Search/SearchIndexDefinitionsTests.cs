using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexDefinitions.Matches"/> decides whether a declared index has to be updated, against the definition
/// the server lists, as MongoDB Search 1.70 lists it:
/// <list type="number">
/// <item>the defaults the server fills in, or leaves out where it lists a definition as created, the order it lists things
/// in, number types and a field's one mapping listed as an array of it are no difference, nor is an element the declaration
/// can't set, which a newer server may add, nor the index's own metadata self-managed mongot lists at a definition's root,
/// its name and type among it;</item>
/// <item>a field, a mapping, an option or a vector field declared otherwise, added or no longer declared is one, the number
/// of partitions, a default overridden and the name of a synonym mapping included.</item>
/// </list>
/// </summary>
public class SearchIndexDefinitionsTests
{
    // Declared as the spike created it, and listed as the server then listed it.
    private const string Declared = """
        { analyzer: 'lucene.standard', mappings: { dynamic: false, fields: {
            A: { type: 'autocomplete' },
            B: { type: 'autocomplete', minGrams: 2, maxGrams: 20, tokenization: 'edgeGram', foldDiacritics: true },
            C: { type: 'objectId' }, D: { type: 'uuid' }, E: { type: 'token', normalizer: 'lowercase' },
            F: { type: 'document', fields: { G: { type: 'token' } } },
            H: { type: 'string', analyzer: 'lucene.turkish', multi: { kw: { type: 'string', analyzer: 'lucene.keyword' } } },
            I: { type: 'date' }, J: { type: 'boolean' }, K: { type: 'number', representation: 'int64' },
            L: { type: 'embeddedDocuments', fields: { M: { type: 'token' } } },
            N: [{ type: 'string' }, { type: 'token' }] } } }
        """;

    private const string Listed = """
        { analyzer: 'lucene.standard', mappings: { dynamic: false, fields: {
            A: { type: 'autocomplete', minGrams: 2, maxGrams: 15, foldDiacritics: true, tokenization: 'edgeGram' },
            B: { type: 'autocomplete', minGrams: 2, maxGrams: 20, foldDiacritics: true, tokenization: 'edgeGram' },
            C: { type: 'objectId' }, D: { type: 'uuid' }, E: { type: 'token', normalizer: 'lowercase' },
            F: { type: 'document', dynamic: false, fields: { G: { type: 'token' } } },
            H: { type: 'string', analyzer: 'lucene.turkish', indexOptions: 'offsets', store: true, norms: 'include',
                 multi: { kw: { type: 'string', analyzer: 'lucene.keyword', indexOptions: 'offsets', store: true, norms: 'include' } } },
            I: { type: 'date' }, J: { type: 'boolean' },
            K: { type: 'number', representation: 'int64', indexDoubles: true, indexIntegers: true },
            L: { type: 'embeddedDocuments', dynamic: false, fields: { M: { type: 'token' } } },
            N: [{ type: 'token' }, { type: 'string', indexOptions: 'offsets', store: true, norms: 'include' }] } } }
        """;

    private const string Vector = """
        { fields: [{ type: 'vector', path: 'Vec', numDimensions: 3, similarity: 'cosine' }, { type: 'filter', path: 'Tenant' }] }
        """;

    // As InsurUp/core declares a field, and mongodb-community-search 1.70.1 lists it: as created, without the defaults.
    private const string AsCreated = """
        { analyzer: 'lucene.turkish', mappings: { dynamic: false, fields: { FileName: [
            { type: 'string', analyzer: 'lucene.turkish', multi: { keyword: { type: 'string', analyzer: 'lucene.keyword' } } },
            { type: 'autocomplete', tokenization: 'edgeGram', minGrams: 2, maxGrams: 20 },
            { type: 'token' }] } } }
        """;

    // As self-managed mongot (MongoDB Community 8.2) lists them: the index's metadata at the root, next to its definition.
    private const string SelfManagedSearch = """
        { indexID: { $oid: '68f1c0de4a5b6c7d8e9f0a1b' }, name: 'default', database: 'PolicyManagement',
          lastObservedCollectionName: 'Policies', collectionUUID: { $binary: { base64: 'ABEiM0RVZneImaq7zN3u/w==', subType: '04' } },
          numPartitions: 1, analyzer: 'lucene.turkish', searchAnalyzer: 'lucene.turkish', indexFeatureVersion: 4,
          mappings: { dynamic: false, fields: { Number: { type: 'token' } } } }
        """;

    private const string SelfManagedVector = """
        { type: 'vectorSearch', indexID: { $oid: '68f1c0de4a5b6c7d8e9f0a1c' }, name: 'vectors', database: 'PolicyManagement',
          lastObservedCollectionName: 'Policies', collectionUUID: { $binary: { base64: 'ABEiM0RVZneImaq7zN3u/w==', subType: '04' } },
          numPartitions: 1, fields: [{ type: 'filter', path: 'Tenant' }, { type: 'vector', path: 'Vec', numDimensions: 3, similarity: 'cosine' }],
          indexFeatureVersion: 3 }
        """;

    [Test]
    [Arguments(Declared, Listed)]
    [Arguments("{ mappings: { dynamic: true } }", "{ mappings: { dynamic: true, fields: { } } }")]
    [Arguments("{ mappings: { dynamic: { typeSet: 'ts' } }, typeSets: [{ name: 'ts', types: [{ type: 'token' }, { type: 'number' }] }] }",
        "{ mappings: { dynamic: { typeSet: 'ts' }, fields: { } }, typeSets: [{ name: 'ts', types: [{ type: 'token' }, { type: 'number', representation: 'double', indexDoubles: true, indexIntegers: true }] }] }")]
    [Arguments("{ mappings: { dynamic: false, fields: { T: [{ type: 'token' }] } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token', futureDefault: true } } } }")]
    public async Task Matches_ListedAsTheServerListsIt_IsNoDifference(string declared,
        string listed)
    {
        // Act
        var matches = SearchIndexDefinitions.Matches(BsonDocument.Parse(declared), BsonDocument.Parse(listed), SearchIndexType.Search);

        // Assert
        await Assert.That(matches).IsTrue();
    }

    [Test]
    [Arguments(AsCreated, AsCreated)]
    [Arguments("{ mappings: { dynamic: false, fields: { T: { type: 'string', store: true } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string' } } } }")]
    [Arguments("{ mappings: { dynamic: false, fields: { F: { type: 'document', fields: { G: { type: 'token' } } } } } }", "{ mappings: { dynamic: false, fields: { F: { type: 'document', fields: { G: { type: 'token' } } } } } }")]
    public async Task Matches_ListedAsCreated_IsNoDifference(string declared,
        string listed)
    {
        // Act
        var matches = SearchIndexDefinitions.Matches(BsonDocument.Parse(declared), BsonDocument.Parse(listed), SearchIndexType.Search);

        // Assert
        await Assert.That(matches).IsTrue();
    }

    [Test]
    [Arguments(SearchIndexType.Search, "{ analyzer: 'lucene.turkish', searchAnalyzer: 'lucene.turkish', mappings: { dynamic: false, fields: { Number: { type: 'token' } } } }", SelfManagedSearch)]
    [Arguments(SearchIndexType.Search, "{ numPartitions: 1, analyzer: 'lucene.turkish', searchAnalyzer: 'lucene.turkish', mappings: { dynamic: false, fields: { Number: { type: 'token' } } } }", SelfManagedSearch)]
    [Arguments(SearchIndexType.VectorSearch, Vector, SelfManagedVector)]
    public async Task Matches_ListedBySelfManagedMongot_IsNoDifference(SearchIndexType type,
        string declared,
        string listed)
    {
        // Act
        var matches = SearchIndexDefinitions.Matches(BsonDocument.Parse(declared), BsonDocument.Parse(listed), type);

        // Assert
        await Assert.That(matches).IsTrue();
    }

    [Test]
    [Arguments("a field added", "{ mappings: { dynamic: false, fields: { T: { type: 'token' }, U: { type: 'token' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("a field no longer declared", "{ mappings: { dynamic: false, fields: { } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("a mapping added", "{ mappings: { dynamic: false, fields: { T: [{ type: 'token' }, { type: 'boolean' }] } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("an option changed", "{ mappings: { dynamic: false, fields: { T: { type: 'autocomplete', maxGrams: 10 } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'autocomplete', minGrams: 2, maxGrams: 15, foldDiacritics: true, tokenization: 'edgeGram' } } } }")]
    [Arguments("an option no longer declared", "{ mappings: { dynamic: false, fields: { T: { type: 'string' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string', analyzer: 'lucene.turkish', indexOptions: 'offsets', store: true, norms: 'include' } } } }")]
    [Arguments("dynamic changed", "{ mappings: { dynamic: true } }", "{ mappings: { dynamic: false, fields: { } } }")]
    [Arguments("a type changed", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string', indexOptions: 'offsets', store: true, norms: 'include' } } } }")]
    [Arguments("a default overridden", "{ mappings: { dynamic: false, fields: { T: { type: 'string', store: false } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string' } } } }")]
    [Arguments("an overridden default no longer declared", "{ mappings: { dynamic: false, fields: { T: { type: 'string' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string', store: false } } } }")]
    [Arguments("partitions changed","{ numPartitions: 2, mappings: { dynamic: true } }", "{ name: 'default', numPartitions: 1, mappings: { dynamic: true, fields: { } } }")]
    [Arguments("partitions no longer declared", "{ mappings: { dynamic: true } }", "{ name: 'default', numPartitions: 2, mappings: { dynamic: true, fields: { } } }")]
    [Arguments("a synonym mapping renamed", "{ mappings: { dynamic: true }, synonyms: [{ name: 'a', analyzer: 'lucene.standard', source: { collection: 'S' } }] }", "{ name: 'default', mappings: { dynamic: true, fields: { } }, synonyms: [{ name: 'b', analyzer: 'lucene.standard', source: { collection: 'S' } }] }")]
    public async Task Matches_DeclaredOtherwise_IsADifference(string difference,
        string declared,
        string listed)
    {
        // Act
        var matches = SearchIndexDefinitions.Matches(BsonDocument.Parse(declared), BsonDocument.Parse(listed), SearchIndexType.Search);

        // Assert
        await Assert.That(matches).IsFalse();
    }

    [Test]
    [Arguments("{ fields: [{ type: 'filter', path: 'Tenant' }, { type: 'vector', path: 'Vec', numDimensions: 3.0, similarity: 'cosine' }] }", true)]
    [Arguments("{ fields: [{ type: 'vector', path: 'Vec', numDimensions: 3, similarity: 'cosine' }] }", false)]
    [Arguments("{ fields: [{ type: 'vector', path: 'Vec', numDimensions: 4, similarity: 'cosine' }, { type: 'filter', path: 'Tenant' }] }", false)]
    public async Task Matches_VectorIndex_ComparesItsFieldsInAnyOrder(string listed,
        bool expected)
    {
        // Act
        var matches = SearchIndexDefinitions.Matches(BsonDocument.Parse(Vector), BsonDocument.Parse(listed), SearchIndexType.VectorSearch);

        // Assert
        await Assert.That(matches).IsEqualTo(expected);
    }
}
