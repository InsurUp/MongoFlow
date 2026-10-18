using MongoDB.Bson;

namespace MongoFlow.Tests.Indexes;

public partial class IndexSpecsTests
{
    // The other index a vault declares of the collection, beside the one planned.
    private const string AlsoDeclared = "by_customer_declared";

    [Test]
    [MethodDataSource(nameof(Synced))]
    public async Task PlanSync_ListedOrNot_PlansWhatSyncingDoes(string name,
        string declared,
        string? listed)
    {
        // Arrange
        var index = Declared(declared);
        IReadOnlyList<BsonDocument> indexes = listed is null
            ? [BsonDocument.Parse("{ v: 2, key: { _id: 1 }, name: '_id_' }")]
            : [BsonDocument.Parse("{ v: 2, key: { _id: 1 }, name: '_id_' }"), BsonDocument.Parse(listed)];

        // Act
        var plan = IndexSpecs.PlanSync(index, indexes, collation: null, new HashSet<string> { index.Name, AlsoDeclared });

        // Assert
        await Verify(new { plan.Action, plan.Changes, plan.Conflicts, Replaces = plan.Replaces?["name"] }).UseParameters(name);
    }

    [Test]
    public async Task Undeclared_ListedIndexes_AreThoseNoVaultDeclaresButTheServersOwn()
    {
        // Arrange — declared, recreated under its name and under another, the server's own, and one no vault declares.
        IReadOnlyList<BsonDocument> listed =
        [
            BsonDocument.Parse("{ v: 2, key: { _id: 1 }, name: '_id_' }"),
            BsonDocument.Parse("{ v: 2, key: { __safeContent__: 1 }, name: '__safeContent___1' }"),
            BsonDocument.Parse("{ v: 2, key: { a: 1 }, name: 'a_1' }"),
            BsonDocument.Parse("{ v: 2, key: { n: 1 }, name: 'n_1', unique: true }"),
            BsonDocument.Parse("{ v: 2, key: { c: 1 }, name: 'by_customer' }"),
            BsonDocument.Parse("{ v: 2, key: { b: 1 }, name: 'b_1' }")
        ];
        var names = new HashSet<string> { "a_1", "n_1", "c_1" };
        IReadOnlyList<IndexPlan> plans =
        [
            IndexSpecs.PlanSync(Declared("{ name: 'a_1', key: { a: 1 } }"), listed, null, names),
            IndexSpecs.PlanSync(Declared("{ name: 'n_1', key: { n: 1 } }"), listed, null, names),
            IndexSpecs.PlanSync(Declared("{ name: 'c_1', key: { c: 1 } }"), listed, null, names)
        ];

        // Act
        var undeclared = IndexSpecs.Undeclared(listed, plans);

        // Assert
        await Assert.That(undeclared.Select(index => index["name"].AsString)).IsEquivalentTo(["b_1"]);
    }

    [Test]
    [Arguments("{ c: 'hashed' }", "{ c: 'hashed' }", true)]
    [Arguments("{ a: 1, b: 1 }", "{ a: 1 }", true)]
    [Arguments("{ a: NumberLong(1) }", "{ a: 1 }", true)]
    [Arguments("{ c: 1 }", "{ c: 'hashed' }", false)]
    [Arguments("{ b: 1, a: 1 }", "{ a: 1 }", false)]
    [Arguments("{ a: 1 }", "{ a: 1, b: 1 }", false)]
    public async Task Backs_IndexKeys_WhenTheyStartWithTheShardKey(string keys,
        string shardKey,
        bool expected)
    {
        // Act
        var backs = IndexSpecs.Backs(BsonDocument.Parse(keys), BsonDocument.Parse(shardKey));

        // Assert
        await Assert.That(backs).IsEqualTo(expected);
    }

    public static IEnumerable<Func<(string, string, string?)>> Synced()
    {
        yield return () => ("as declared", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: 1 }, name: 'a_1' }");
        yield return () => ("hidden", "{ name: 'a_1', key: { a: 1 }, hidden: true }", "{ v: 2, key: { a: 1 }, name: 'a_1' }");
        yield return () => ("made unique", "{ name: 'a_1', key: { a: 1 }, unique: true }", "{ v: 2, key: { a: 1 }, name: 'a_1' }");
        yield return () => ("made unique and shown", "{ name: 'a_1', key: { a: 1 }, unique: true }",
            "{ v: 2, key: { a: 1 }, name: 'a_1', hidden: true }");
        yield return () => ("made unique and sparse", "{ name: 'a_1', key: { a: 1 }, unique: true, sparse: true }",
            "{ v: 2, key: { a: 1 }, name: 'a_1' }");
        yield return () => ("unique no longer", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: 1 }, name: 'a_1', unique: true }");
        yield return () => ("keys", "{ name: 'a_1', key: { a: 1 } }", "{ v: 2, key: { a: -1 }, name: 'a_1' }");
        yield return () => ("expiring, and declared not to", "{ name: 'd_1', key: { d: 1 } }",
            "{ v: 2, key: { d: 1 }, name: 'd_1', expireAfterSeconds: 3600 }");
        yield return () => ("under another name", "{ name: 'Customer_1', key: { Customer: 1 } }",
            "{ v: 2, key: { Customer: 1 }, name: 'by_customer', hidden: true }");
        yield return () => ("under another name declared too", "{ name: 'Customer_1', key: { Customer: 1 } }",
            $"{{ v: 2, key: {{ Customer: 1 }}, name: '{AlsoDeclared}' }}");
        yield return () => ("missing", "{ name: 'a_1', key: { a: 1 } }", null);
    }
}
