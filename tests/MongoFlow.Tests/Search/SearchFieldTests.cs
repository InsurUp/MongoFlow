using MongoDB.Bson;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchField"/> writes a field's mapping as MongoDB Search takes it: each type with the options given, text
/// with its alternates, a mapping written by hand as it is, and arguments MongoDB can't take refused.
/// </summary>
public class SearchFieldTests
{
    [Test]
    public async Task Definition_EachType_IsWrittenAsMongoDbSearchTakesIt()
    {
        // Act
        var definitions = new[]
        {
            SearchField.String(),
            SearchField.String("lucene.turkish", "lucene.standard"),
            SearchField.Autocomplete(),
            SearchField.Autocomplete(3, 10, AutocompleteTokenization.NGram, foldDiacritics: false, analyzer: "lucene.keyword"),
            SearchField.Autocomplete(tokenization: AutocompleteTokenization.RightEdgeGram),
            SearchField.Token(),
            SearchField.Token(lowercase: true),
            SearchField.Number(),
            SearchField.Date(),
            SearchField.Boolean(),
            SearchField.ObjectId(),
            SearchField.Uuid(),
            SearchField.Raw(BsonDocument.Parse("{ type: 'geo', indexShapes: true }"))
        }.Select(field => field.Definition);

        // Assert
        await Verify(definitions);
    }

    [Test]
    public async Task Multi_TextAlternates_AreWrittenUnderTheTextsMulti()
    {
        // Act — the second keeps the alternate written by hand beside the one added.
        var definitions = new[]
        {
            SearchField.String("lucene.turkish")
                .Multi("keyword", SearchField.String("lucene.keyword"))
                .Multi("english", SearchField.String("lucene.english", "lucene.standard")),
            SearchField.Raw(BsonDocument.Parse("{ type: 'string', multi: { keyword: { type: 'string', analyzer: 'lucene.keyword' } } }"))
                .Multi("english", SearchField.String("lucene.english"))
        }.Select(field => field.Definition);

        // Assert
        await Verify(definitions);
    }

    [Test]
    public async Task Multi_OfAMapping_LeavesItAsItWas()
    {
        // Arrange
        var field = SearchField.String();

        // Act
        field.Multi("keyword", SearchField.String("lucene.keyword"));

        // Assert
        await Assert.That(field.Definition.Contains("multi")).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(AlternatesMongoDbCantTake))]
    public async Task Multi_AlternateMongoDbCantTake_ThrowsArgumentException(string alternate,
        Func<SearchField> multi)
    {
        // Act & Assert
        await Assert.That(() => multi()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Multi_NullAlternate_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => SearchField.String().Multi("keyword", null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Raw_DefinitionChangedAfterwards_KeepsItAsGiven()
    {
        // Arrange
        var definition = new BsonDocument("type", "token");
        var field = SearchField.Raw(definition);

        // Act
        definition["type"] = "string";

        // Assert
        await Assert.That(field.Type).IsEqualTo("token");
    }

    [Test]
    [Arguments("{ indexShapes: true }")]
    [Arguments("{ type: 1 }")]
    public async Task Raw_WithoutAType_ThrowsArgumentException(string definition)
    {
        // Act & Assert
        await Assert.That(() => SearchField.Raw(BsonDocument.Parse(definition))).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Raw_Null_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => SearchField.Raw(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(0, 15)]
    [Arguments(5, 4)]
    public async Task Autocomplete_GramsMongoDbCantTake_ThrowsArgumentOutOfRangeException(int minGrams,
        int maxGrams)
    {
        // Act & Assert
        await Assert.That(() => SearchField.Autocomplete(minGrams, maxGrams)).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    public static IEnumerable<Func<(string, Func<SearchField>)>> AlternatesMongoDbCantTake()
    {
        var keyword = SearchField.String("lucene.keyword");

        yield return () => ("of a token mapping", () => SearchField.Token().Multi("keyword", keyword));
        yield return () => ("a token", () => SearchField.String().Multi("exact", SearchField.Token()));
        yield return () => ("with alternates of its own", () => SearchField.String().Multi("keyword", keyword.Multi("english", keyword)));
        yield return () => ("of a name taken", () => SearchField.String().Multi("keyword", keyword).Multi("keyword", keyword));
        yield return () => ("of a blank name", () => SearchField.String().Multi(" ", keyword));
    }
}
