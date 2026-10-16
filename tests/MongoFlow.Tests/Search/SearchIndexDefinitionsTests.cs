using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexDefinitions.Matches"/> decides whether a declared index has to be updated, against the definition
/// the server lists, as MongoDB Search 1.70 lists it:
/// <list type="number">
/// <item>the defaults the server fills in, the order it lists things in, number types and a field's one mapping listed as
/// an array of it are no difference, nor is an element the declaration can't set, which a newer server may add;</item>
/// <item>a field, a mapping, an option or a vector field declared otherwise, added or no longer declared is one.</item>
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
    [Arguments("a field added", "{ mappings: { dynamic: false, fields: { T: { type: 'token' }, U: { type: 'token' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("a field no longer declared", "{ mappings: { dynamic: false, fields: { } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("a mapping added", "{ mappings: { dynamic: false, fields: { T: [{ type: 'token' }, { type: 'boolean' }] } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }")]
    [Arguments("an option changed", "{ mappings: { dynamic: false, fields: { T: { type: 'autocomplete', maxGrams: 10 } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'autocomplete', minGrams: 2, maxGrams: 15, foldDiacritics: true, tokenization: 'edgeGram' } } } }")]
    [Arguments("an option no longer declared", "{ mappings: { dynamic: false, fields: { T: { type: 'string' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string', analyzer: 'lucene.turkish', indexOptions: 'offsets', store: true, norms: 'include' } } } }")]
    [Arguments("dynamic changed", "{ mappings: { dynamic: true } }", "{ mappings: { dynamic: false, fields: { } } }")]
    [Arguments("a type changed", "{ mappings: { dynamic: false, fields: { T: { type: 'token' } } } }", "{ mappings: { dynamic: false, fields: { T: { type: 'string', indexOptions: 'offsets', store: true, norms: 'include' } } } }")]
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
