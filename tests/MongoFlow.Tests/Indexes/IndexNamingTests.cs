using MongoDB.Bson;

namespace MongoFlow.Tests.Indexes;

/// <summary>
/// An index without a name is named as the server and the driver name one, so a declaration matches an index created
/// without a name: each key's field and value joined by underscores, numbers in the invariant culture.
/// </summary>
public class IndexNamingTests
{
    [Test]
    [Arguments("{ Customer: 1 }", "Customer_1")]
    [Arguments("{ Customer: 1, PlacedAt: -1 }", "Customer_1_PlacedAt_-1")]
    [Arguments("{ a: 1.0, b: NumberLong(-1) }", "a_1_b_-1")]
    [Arguments("{ t: 'text', u: 'text', k: 1 }", "t_text_u_text_k_1")]
    [Arguments("{ h: 'hashed' }", "h_hashed")]
    [Arguments("{ g: '2dsphere' }", "g_2dsphere")]
    [Arguments("{ 'w.$**': 1 }", "w.$**_1")]
    [Arguments("{ 'first name': 1 }", "first_name_1")]
    [Arguments("{ a: true }", "a_x")]
    public async Task Of_Keys_NamesThemAsTheServerDoes(string keys,
        string expected)
    {
        // Act
        var name = IndexNaming.Of(BsonDocument.Parse(keys));

        // Assert
        await Assert.That(name).IsEqualTo(expected);
    }
}
