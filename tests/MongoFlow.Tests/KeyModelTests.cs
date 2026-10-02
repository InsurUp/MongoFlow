using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="KeyModel{TDocument,TKey}"/>: a keyed collection reads a document's key and matches a document by key as
/// BSON, with each member under its element name and serialized like the member, so a write by key needs no LINQ:
/// <list type="number">
/// <item>without a configured key, the key is the member mapped to <c>_id</c>;</item>
/// <item>a member key matches that member;</item>
/// <item>a composite key matches each member with the key property named like its constructor parameter.</item>
/// </list>
/// Keys that can't be matched this way are rejected when the model is built; see <c>KeyModelTests.Errors.cs</c>.
/// </summary>
public partial class KeyModelTests
{
    [Test]
    public async Task Match_DefaultKey_MatchesTheIdElement()
    {
        // Arrange
        var id = ObjectId.Parse("650000000000000000000001");
        var model = KeyModel<Policy, ObjectId>.Create(null, Collection<Policy>());

        // Act
        var filter = model.Match(id);

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_MemberKey_MatchesItsElementName()
    {
        // Arrange
        var model = KeyModel<Policy, string>.Create(p => p.Number, Collection<Policy>());

        // Act
        var filter = model.Match("P-1");

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_MemberKey_SerializesTheValueLikeTheMember()
    {
        // Arrange
        var model = KeyModel<Shipment, ShipmentStatus>.Create(s => s.Status, Collection<Shipment>());

        // Act
        var filter = model.Match(ShipmentStatus.InTransit);

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_GuidKey_SerializesItWithItsRepresentation()
    {
        // Arrange
        var id = Guid.Parse("8f5b6c1e-3c2a-4e7d-9b1a-2d4c6e8f0a1b");
        var model = KeyModel<Device, Guid>.Create(null, Collection<Device>());

        // Act
        var filter = model.Match(id);

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_CompositeKey_MatchesEveryMember()
    {
        // Arrange
        var model = KeyModel<LoginToken, TokenKey>.Create(t => new TokenKey(t.UserId, t.Provider), Collection<LoginToken>());

        // Act
        var filter = model.Match(new TokenKey("u-1", "github"));

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_CompositeKeyWithCamelCaseParameters_MatchesTheProperties()
    {
        // Arrange
        var model = KeyModel<LoginToken, CamelCaseTokenKey>.Create(t => new CamelCaseTokenKey(t.UserId, t.Provider),
            Collection<LoginToken>());

        // Act
        var filter = model.Match(new CamelCaseTokenKey("u-1", "github"));

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Match_CompositeKeyWithPropertiesDifferingInCase_MatchesThePropertyNamedLikeTheParameter()
    {
        // Arrange
        var model = KeyModel<LoginToken, CaseVariantKey>.Create(t => new CaseVariantKey(t.UserId, t.Provider),
            Collection<LoginToken>());

        // Act
        var filter = model.Match(new CaseVariantKey("u-1", "github"));

        // Assert
        await Verify(filter);
    }

    [Test]
    public async Task Get_CompositeKey_ReadsTheDocumentsKey()
    {
        // Arrange
        const string UserId = "u-1";
        const string Provider = "github";
        var model = KeyModel<LoginToken, TokenKey>.Create(t => new TokenKey(t.UserId, t.Provider), Collection<LoginToken>());

        // Act
        var key = model.Get(new LoginToken { UserId = UserId, Provider = Provider });

        // Assert
        await Assert.That(key).IsEqualTo(new TokenKey(UserId, Provider));
    }

    private static IMongoCollection<TDocument> Collection<TDocument>() => Offline.Database.GetCollection<TDocument>("keys");
}
