using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchIndexMapping"/> reads what a search index maps, to decide which comparisons it can serve as MQL does:
/// <list type="number">
/// <item>equality needs a type matching the value: a token that doesn't lowercase for strings, a number indexing the
/// value's kind, exactly for 64-bit integers beyond 2^53 only as int64;</item>
/// <item>null and existence need an explicit exact mapping, which indexes nulls;</item>
/// <item>dynamic mappings serve what they index, strings as analyzed text, which equality can't; a type set serves its
/// types; a document mapping decides for the fields inside it, and embedded documents serve nothing;</item>
/// <item>a vector index serves its filter fields.</item>
/// </list>
/// </summary>
public class SearchIndexMappingTests
{
    private static readonly SearchIndexMapping Static = SearchIndexMapping.Parse(BsonDocument.Parse("""
        { mappings: { fields: {
            Name: [{ type: 'string' }, { type: 'token' }],
            Email: { type: 'token', normalizer: 'lowercase' },
            Total: { type: 'number', indexDoubles: false },
            Count: { type: 'number', representation: 'int64', indexIntegers: false },
            PlacedAt: { type: 'date' },
            IsDeleted: { type: 'boolean' },
            Reference: { type: 'uuid' },
            Address: { type: 'document', dynamic: true, fields: { Notes: { type: 'string' } } },
            Lines: { type: 'embeddedDocuments', dynamic: true } } } }
        """), SearchIndexType.Search);

    private static readonly SearchIndexMapping Dynamic = SearchIndexMapping.Parse(BsonDocument.Parse("""
        { mappings: { dynamic: true, fields: { Hidden: { type: 'document', fields: { } } } } }
        """), SearchIndexType.Search);

    private static readonly SearchIndexMapping TypeSet = SearchIndexMapping.Parse(BsonDocument.Parse("""
        { mappings: { dynamic: { typeSet: 'exact' } }, typeSets: [{ name: 'exact', types: [{ type: 'token' }] }] }
        """), SearchIndexType.Search);

    [Test]
    [Arguments("Name", "'ada'", true)]
    [Arguments("Name", "1", false)]
    [Arguments("Email", "'ada'", false)]
    [Arguments("Total", "1", true)]
    [Arguments("Total", "1.5", false)]
    [Arguments("Total", "NumberLong('9007199254740993')", false)]
    [Arguments("Total", "NumberLong('9007199254740992')", true)]
    [Arguments("Count", "NumberLong('9007199254740993')", false)]
    [Arguments("PlacedAt", "ISODate('2026-01-01T00:00:00Z')", true)]
    [Arguments("IsDeleted", "true", true)]
    [Arguments("Reference", "UUID('3a1f2b9e-0000-4000-8000-000000000001')", true)]
    [Arguments("Reference", "BinData(3, 'AAAAAAAAAAAAAAAAAAAAAA==')", false)]
    [Arguments("Name", "null", true)]
    [Arguments("Address.Notes", "null", false)]
    [Arguments("Address.Number", "7", true)]
    [Arguments("Address.Notes", "'x'", false)]
    [Arguments("Lines.Number", "7", false)]
    [Arguments("Missing", "'x'", false)]
    public async Task CanEqual_ValueOfAStaticMapping_ServesWhatItsTypeCompares(string path,
        string value,
        bool expected)
    {
        // Act
        var can = Static.CanEqual(path, Value(value));

        // Assert
        await Assert.That(can).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Total", "1", true)]
    [Arguments("Count", "NumberLong(1)", false)]
    [Arguments("Name", "'a'", true)]
    [Arguments("Email", "'a'", false)]
    [Arguments("IsDeleted", "true", false)]
    [Arguments("Reference", "UUID('3a1f2b9e-0000-4000-8000-000000000001')", false)]
    public async Task CanRange_ValueOfAStaticMapping_ServesOrderedTypes(string path,
        string value,
        bool expected)
    {
        // Act
        var can = Static.CanRange(path, Value(value));

        // Assert
        await Assert.That(can).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Total", "1", true)]
    [Arguments("IsDeleted", "true", true)]
    [Arguments("Name", "'ada'", false)]
    [Arguments("Name", "null", false)]
    [Arguments("Hidden.Total", "1", false)]
    public async Task CanEqual_DynamicMapping_ServesWhatItIndexesExactly(string path,
        string value,
        bool expected)
    {
        // Act
        var can = Dynamic.CanEqual(path, Value(value));

        // Assert
        await Assert.That(can).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Name", "'ada'", true)]
    [Arguments("Total", "1", false)]
    public async Task CanEqual_TypeSetMapping_ServesItsTypes(string path,
        string value,
        bool expected)
    {
        // Act
        var can = TypeSet.CanEqual(path, Value(value));

        // Assert
        await Assert.That(can).IsEqualTo(expected);
    }

    [Test]
    public async Task IsVectorFilter_FieldsOfAVectorIndex_AreItsFilterFields()
    {
        // Arrange
        var mapping = SearchIndexMapping.Parse(BsonDocument.Parse("""
            { fields: [{ type: 'vector', path: 'Embedding', numDimensions: 3, similarity: 'cosine' }, { type: 'filter', path: 'TenantId' }] }
            """), SearchIndexType.VectorSearch);

        // Act
        var filters = new[] { "TenantId", "Embedding", "Missing" }.Where(mapping.IsVectorFilter);

        // Assert
        await Assert.That(filters).IsEquivalentTo(["TenantId"]);
    }

    [Test]
    public async Task CanEqual_DefinitionWithoutMappings_ServesNothing()
    {
        // Arrange
        var mapping = SearchIndexMapping.Parse([], SearchIndexType.Search);

        // Act
        var can = mapping.CanEqual("TenantId", "t-1");

        // Assert
        await Assert.That(can).IsFalse();
    }

    private static BsonValue Value(string json) => BsonDocument.Parse($"{{ v: {json} }}")["v"];
}
