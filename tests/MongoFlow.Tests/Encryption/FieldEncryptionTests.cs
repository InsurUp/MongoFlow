using MongoDB.Bson;
using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

/// <summary>
/// <see cref="FieldEncryption"/> encrypts a collection's encrypted members in what MongoFlow sends:
/// <list type="number">
/// <item>documents get each encrypted value encrypted to be stored, in place, and lose encrypted members that are null;</item>
/// <item>filters get the values they compare encrypted members with encrypted: equality forms one by one, comparisons of a
/// range field as one expression, null as absence, at any depth of <c>$and</c>, <c>$or</c> and <c>$nor</c>;</item>
/// <item>updates get the values <c>$set</c> sets encrypted members to encrypted, inside a document set whole too, and
/// null unset;</item>
/// <item>filters and updates that touch no encrypted member come back as they were, the same instance;</item>
/// <item>what the server can't do to an encrypted member fails before anything is sent.</item>
/// </list>
/// </summary>
public partial class FieldEncryptionTests
{
    [Test]
    public async Task DocumentAsync_EncryptedMembers_EncryptsThemAndLeavesNullsOut()
    {
        // Arrange
        var document = BsonDocument.Parse(
            "{ _id: 1, Name: 'Ada', Ssn: '111-11', Salary: 5000, Notes: null, Address: { City: 'London', Street: 'Baker Street' } }");

        // Act
        var encrypted = await Encryption().DocumentAsync(document, CancellationToken.None);

        // Assert
        await Verify(new { encrypted, InPlace = ReferenceEquals(encrypted, document) });
    }

    [Test]
    public async Task FilterAsync_EqualityComparisons_EncryptTheValuesCompared()
    {
        // Arrange
        var filter = BsonDocument.Parse("""
            {
              Ssn: '111-11',
              Name: 'Ada',
              $or: [{ Ssn: { $in: ['222-22', '333-33'] } }, { Ssn: { $ne: '444-44', $exists: true } }],
              $nor: [{ Ssn: { $nin: ['555-55'] } }, { Ssn: { $eq: null } }],
              'Address.Street': { $ne: null },
              Notes: null
            }
            """);

        // Act
        var encrypted = await Encryption().FilterAsync(filter, CancellationToken.None);

        // Assert
        await Verify(encrypted);
    }

    [Test]
    public async Task FilterAsync_RangeComparisons_EncryptThemAsExpressions()
    {
        // Arrange — the bounds go in one expression; equality is a range of one value.
        var filter = BsonDocument.Parse("""
            {
              Salary: { $gt: 1000, $lte: 3000 },
              Name: 'Ada',
              $or: [{ Salary: 2000 }, { Salary: { $in: [4000, 5000] } }, { Salary: { $ne: 6000, $nin: [7000] } }, { Salary: { $exists: true, $lt: 8000 } }]
            }
            """);

        // Act
        var encrypted = await Encryption().FilterAsync(filter, CancellationToken.None);

        // Assert
        await Verify(encrypted);
    }

    [Test]
    public async Task FilterAsync_RangeBesideAnAnd_JoinsThemInAnotherAnd()
    {
        // Arrange — a range compares through a clause of its own, which can't be a field beside the filter's $and.
        var filter = BsonDocument.Parse("{ $and: [{ Name: 'Ada' }], Salary: { $gte: 1000 } }");

        // Act
        var encrypted = await Encryption().FilterAsync(filter, CancellationToken.None);

        // Assert
        await Verify(encrypted);
    }

    [Test]
    public async Task FilterAsync_RangeAlone_IsTheClause()
    {
        // Arrange
        var filter = BsonDocument.Parse("{ Salary: { $lt: 1000 } }");

        // Act
        var encrypted = await Encryption().FilterAsync(filter, CancellationToken.None);

        // Assert
        await Verify(encrypted);
    }

    [Test]
    public async Task FilterAsync_NoEncryptedMember_ReturnsTheFilterItself()
    {
        // Arrange — a save shares its rendered query filters between operations, so they mustn't be copied or changed.
        var filter = BsonDocument.Parse("{ Name: 'Ada', $or: [{ Total: { $gt: 10 } }, { $expr: { $eq: ['$Name', 'Bob'] } }], $comment: 'x' }");

        // Act
        var encrypted = await Encryption().FilterAsync(filter, CancellationToken.None);

        // Assert
        await Assert.That(encrypted).IsSameReferenceAs(filter);
    }

    [Test]
    [Arguments("OrderOfAnEqualityField", "{ Ssn: { $gt: '1' } }")]
    [Arguments("RegularExpression", "{ Ssn: /^1/ }")]
    [Arguments("NullAmongValues", "{ Ssn: { $in: [null, '1'] } }")]
    [Arguments("OtherOperatorOnARangeField", "{ Salary: { $mod: [2, 0] } }")]
    [Arguments("FieldWithoutQueries", "{ Notes: 'paid' }")]
    [Arguments("DocumentHoldingAnEncryptedField", "{ Address: { City: 'London', Street: 'Baker Street' } }")]
    [Arguments("Expression", "{ $expr: { $eq: ['$Ssn', '111-11'] } }")]
    public async Task FilterAsync_ComparisonTheServerCantMake_ThrowsNotSupportedException(string comparison,
        string filter)
    {
        // Act & Assert
        await ThrowsValueTask(() => Encryption().FilterAsync(BsonDocument.Parse(filter), CancellationToken.None))
            .IgnoreStackTrace()
            .UseParameters(comparison);
    }

    [Test]
    public async Task UpdateAsync_SetsOfEncryptedMembers_EncryptsThem()
    {
        // Arrange — Notes set to null is unset; the address, set whole, has its street encrypted inside it.
        var update = BsonDocument.Parse("""
            {
              $set: { Name: 'Ada', Ssn: '999-99', Notes: null, Address: { City: 'Paris', Street: 'Rue de Rivoli' } },
              $unset: { Salary: '' },
              $inc: { Version: 1 }
            }
            """);

        // Act
        var encrypted = await Encryption().UpdateAsync(update, CancellationToken.None);

        // Assert
        await Verify(new { encrypted, Original = update });
    }

    [Test]
    public async Task UpdateAsync_NoEncryptedMember_ReturnsTheUpdateItself()
    {
        // Arrange
        var update = BsonDocument.Parse("{ $set: { Name: 'Ada', 'Address.City': 'Paris' }, $unset: { Ssn: '' }, $inc: { Version: 1 } }");

        // Act
        var encrypted = await Encryption().UpdateAsync(update, CancellationToken.None);

        // Assert
        await Assert.That(encrypted).IsSameReferenceAs(update);
    }

    [Test]
    [Arguments("OtherOperator", "{ $inc: { Salary: 1 } }")]
    [Arguments("SetInsideAnEncryptedField", "{ $set: { 'Address.Street.Number': 1 } }")]
    [Arguments("OperatorOnADocumentHoldingOne", "{ $rename: { Address: 'Home' } }")]
    [Arguments("Pipeline", "[{ $set: { Name: 'Ada' } }]")]
    public async Task UpdateAsync_ChangeTheServerCantMake_ThrowsNotSupportedException(string change,
        string update)
    {
        // Act & Assert
        await ThrowsValueTask(() => Encryption().UpdateAsync(Parse(update), CancellationToken.None))
            .IgnoreStackTrace()
            .UseParameters(change);
    }

    [Test]
    public async Task DocumentAsync_EncryptedMemberInsideAnArray_ThrowsNotSupportedException()
    {
        // Arrange
        var document = BsonDocument.Parse("{ _id: 1, Address: [{ Street: 'Baker Street' }] }");

        // Act & Assert
        await ThrowsValueTask(() => Encryption().DocumentAsync(document, CancellationToken.None)).IgnoreStackTrace();
    }

    private static BsonValue Parse(string json) =>
        json.StartsWith('[') ? MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonArray>(json) : BsonDocument.Parse(json);
}
