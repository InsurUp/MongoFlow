using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Indexes;

/// <summary>
/// A declared index compared with what <c>listIndexes</c> lists, as captured from MongoDB 8.2:
/// <list type="number">
/// <item>one listed as declared is left, though the server lists its keys' numbers, text keys and collation its own way,
/// and gives it the collection's collation when it declares none;</item>
/// <item>its expiry and whether it's hidden are changed in place, an expiry being added included;</item>
/// <item>its keys, uniqueness, sparseness, partial filter and collation, and an expiry it shouldn't have, conflict, as an
/// index the server keeps one of under another name does;</item>
/// <item>one missing is created.</item>
/// </list>
/// </summary>
public class IndexSpecsTests
{
    private const string Turkish =
        "{ locale: 'tr', caseLevel: false, caseFirst: 'off', strength: 2, numericOrdering: false, alternate: 'non-ignorable', " +
        "maxVariable: 'punct', normalization: false, backwards: false, version: '57.1' }";

    [Test]
    [MethodDataSource(nameof(Unchanged))]
    public async Task Plan_ListedAsDeclared_LeavesIt(string _,
        string declared,
        string listed,
        string? collation)
    {
        // Act
        var plan = IndexSpecs.Plan(Declared(declared), [BsonDocument.Parse(listed)], Parse(collation));

        // Assert
        await Assert.That(plan.Action).IsEqualTo(SetupAction.Unchanged);
    }

    [Test]
    [MethodDataSource(nameof(InPlace))]
    public async Task Plan_ExpiryOrHiddenDiffers_ChangesItInPlace(string name,
        string declared,
        string listed)
    {
        // Act
        var plan = IndexSpecs.Plan(Declared(declared), [BsonDocument.Parse(listed)], collation: null);

        // Assert
        await Verify(new { plan.Action, plan.Changes }).UseParameters(name);
    }

    [Test]
    [MethodDataSource(nameof(Conflicting))]
    public async Task Plan_DiffersInWhatCantChangeInPlace_Conflicts(string name,
        string declared,
        string listed,
        string? collation)
    {
        // Act
        var plan = IndexSpecs.Plan(Declared(declared),
            [BsonDocument.Parse("{ key: { _id: 1 }, name: '_id_' }"), BsonDocument.Parse(listed)],
            Parse(collation));

        // Assert
        await Verify(new { plan.Action, plan.Conflicts }).UseParameters(name);
    }

    [Test]
    public async Task Plan_NoneOfItsNameOrKeys_CreatesIt()
    {
        // Arrange
        var declared = Declared("{ name: 'Customer_1', key: { Customer: 1 } }");

        // Act
        var plan = IndexSpecs.Plan(declared, [BsonDocument.Parse("{ key: { Customer: 1 }, name: 'Customer_unique', unique: true }")],
            collation: null);

        // Assert
        await Assert.That(plan.Action).IsEqualTo(SetupAction.Create);
    }

    public static IEnumerable<Func<(string, string, string, string?)>> Unchanged()
    {
        yield return () => ("numbers of other types", "{ name: 'a_1_b_-1', key: { a: 1.0, b: NumberLong(-1) } }",
            "{ v: 2, key: { a: 1, b: -1 }, name: 'a_1_b_-1' }", null);
        yield return () => ("unique and sparse", "{ name: 'a_1', key: { a: 1 }, unique: true, sparse: true }",
            "{ v: 2, key: { a: 1 }, name: 'a_1', unique: true, sparse: true }", null);
        yield return () => ("partial filter, in another order",
            "{ name: 'p_1', key: { p: 1 }, partialFilterExpression: { status: 'active', n: { $gt: NumberLong(5) } } }",
            "{ v: 2, key: { p: 1 }, name: 'p_1', partialFilterExpression: { n: { $gt: 5 }, status: 'active' } }", null);
        yield return () => ("text, with a field after", "{ name: 't_text_u_text_k_1', key: { t: 'text', u: 'text', k: 1 } }",
            "{ v: 2, key: { _fts: 'text', _ftsx: 1, k: 1 }, name: 't_text_u_text_k_1', weights: { t: 1, u: 1 }, " +
            "default_language: 'english', language_override: 'language', textIndexVersion: 3 }", null);
        yield return () => ("2dsphere", "{ name: 'g_2dsphere', key: { g: '2dsphere' } }",
            "{ v: 2, key: { g: '2dsphere' }, name: 'g_2dsphere', '2dsphereIndexVersion': 3 }", null);
        yield return () => ("collation, as the server fills it in", "{ name: 'name_1', key: { name: 1 }, collation: { locale: 'tr', strength: 2 } }",
            $"{{ v: 2, key: {{ name: 1 }}, name: 'name_1', collation: {Turkish} }}", null);
        yield return () => ("the collection's collation", "{ name: 'n_1', key: { n: 1 } }",
            $"{{ v: 2, key: {{ n: 1 }}, name: 'n_1', collation: {Turkish} }}", Turkish);
        yield return () => ("simple, in a collection with a collation", "{ name: 'm_1', key: { m: 1 }, collation: { locale: 'simple' } }",
            "{ v: 2, key: { m: 1 }, name: 'm_1' }", Turkish);
        yield return () => ("expiring and hidden", "{ name: 'd_1', key: { d: 1 }, expireAfterSeconds: 3600, hidden: true }",
            "{ v: 2, key: { d: 1 }, name: 'd_1', hidden: true, expireAfterSeconds: 3600 }", null);
    }

    public static IEnumerable<Func<(string, string, string)>> InPlace()
    {
        yield return () => ("expiry changed", "{ name: 'd_1', key: { d: 1 }, expireAfterSeconds: 60 }",
            "{ v: 2, key: { d: 1 }, name: 'd_1', expireAfterSeconds: 3600 }");
        yield return () => ("expiry added", "{ name: 'd_1', key: { d: 1 }, expireAfterSeconds: 60 }", "{ v: 2, key: { d: 1 }, name: 'd_1' }");
        yield return () => ("hidden", "{ name: 'd_1', key: { d: 1 }, hidden: true }", "{ v: 2, key: { d: 1 }, name: 'd_1' }");
        yield return () => ("shown", "{ name: 'd_1', key: { d: 1 } }", "{ v: 2, key: { d: 1 }, name: 'd_1', hidden: true }");
    }

    public static IEnumerable<Func<(string, string, string, string?)>> Conflicting()
    {
        yield return () => ("keys", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: -1 }, name: 'a_1' }", null);
        yield return () => ("text fields", "{ name: 't_text', key: { t: 'text' } }",
            "{ v: 2, key: { _fts: 'text', _ftsx: 1 }, name: 't_text', weights: { t: 1, u: 1 } }", null);
        yield return () => ("made unique", "{ name: 'a_1', key: { a: 1 }, unique: true }", "{ v: 2, key: { a: 1 }, name: 'a_1' }", null);
        yield return () => ("unique no longer", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: 1 }, name: 'a_1', unique: true }", null);
        yield return () => ("made sparse", "{ name: 'a_1', key: { a: 1 }, sparse: true }", "{ v: 2, key: { a: 1 }, name: 'a_1' }", null);
        yield return () => ("sparse no longer", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: 1 }, name: 'a_1', sparse: true }", null);
        yield return () => ("partial filter added", "{ name: 'p_1', key: { p: 1 }, partialFilterExpression: { s: 'active' } }",
            "{ v: 2, key: { p: 1 }, name: 'p_1' }", null);
        yield return () => ("partial filter removed", "{ name: 'p_1', key: { p: 1 } }",
            "{ v: 2, key: { p: 1 }, name: 'p_1', partialFilterExpression: { s: 'active' } }", null);
        yield return () => ("partial filter changed", "{ name: 'p_1', key: { p: 1 }, partialFilterExpression: { s: 'closed' } }",
            "{ v: 2, key: { p: 1 }, name: 'p_1', partialFilterExpression: { s: 'active' } }", null);
        yield return () => ("collation", "{ name: 'name_1', key: { name: 1 }, collation: { locale: 'en' } }",
            $"{{ v: 2, key: {{ name: 1 }}, name: 'name_1', collation: {Turkish} }}", null);
        yield return () => ("collation of a strength", "{ name: 'name_1', key: { name: 1 }, collation: { locale: 'tr' } }",
            $"{{ v: 2, key: {{ name: 1 }}, name: 'name_1', collation: {Turkish} }}", null);
        yield return () => ("the collection's collation, not its own", "{ name: 'n_1', key: { n: 1 } }",
            "{ v: 2, key: { n: 1 }, name: 'n_1' }", Turkish);
        yield return () => ("expiring, and declared not to", "{ name: 'd_1', key: { d: 1 } }",
            "{ v: 2, key: { d: 1 }, name: 'd_1', expireAfterSeconds: 3600 }", null);
        yield return () => ("under another name", "{ name: 'Customer_1', key: { Customer: 1 } }",
            "{ v: 2, key: { Customer: 1 }, name: 'by_customer', hidden: true }", null);
    }

    // A declaration written as the server lists an index.
    private static IndexDeclaration Declared(string json)
    {
        var index = BsonDocument.Parse(json);
        return new IndexDeclaration(index["name"].AsString,
            index["key"].AsBsonDocument,
            index.GetValue("unique", false).ToBoolean(),
            index.GetValue("sparse", false).ToBoolean(),
            index.GetValue("partialFilterExpression", BsonNull.Value) as BsonDocument,
            index.TryGetValue("expireAfterSeconds", out var seconds) ? TimeSpan.FromSeconds(seconds.ToInt64()) : null,
            index.GetValue("hidden", false).ToBoolean(),
            index.GetValue("collation", BsonNull.Value) is BsonDocument collation ? Collation.FromBsonDocument(collation) : null);
    }

    private static BsonDocument? Parse(string? json) => json is null ? null : BsonDocument.Parse(json);
}
