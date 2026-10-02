using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Semver;

namespace MongoFlow.Tests;

/// <summary>
/// Applied migrations are stored as earlier MongoFlow versions stored them, with the version as its string, and read
/// back leniently, ignoring elements the record doesn't have.
/// </summary>
public class SemVersionSerializerTests
{
    [Test]
    public async Task Serialize_Record_WritesTheFormatEarlierVersionsWrote()
    {
        // Arrange
        var record = new MigrationRecord
        {
            Id = ObjectId.Parse("66f0c0ffee0000000000abcd"),
            Version = SemVersion.Parse("1.2.0-beta.1"),
            Name = "RenamePremium",
            Description = "Rename the premium field",
            Timestamp = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var document = record.ToBsonDocument();

        // Assert
        await Verify(document);
    }

    [Test]
    public async Task Deserialize_LooseVersionAndAnUnknownElement_ReadsTheVersion()
    {
        // Arrange
        var document = new BsonDocument
        {
            ["_id"] = ObjectId.Parse("66f0c0ffee0000000000abcd"),
            ["Version"] = "v1.2",
            ["Name"] = "RenamePremium",
            ["Timestamp"] = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            ["AppliedBy"] = "a tool"
        };

        // Act
        var record = BsonSerializer.Deserialize<MigrationRecord>(document);

        // Assert
        await Assert.That(record.Version.ToString()).IsEqualTo("1.2.0");
    }

    [Test]
    public async Task Deserialize_VersionThatIsNotAString_ThrowsFormatException()
    {
        // Arrange
        var document = new BsonDocument
        {
            ["_id"] = ObjectId.Parse("66f0c0ffee0000000000abcd"),
            ["Version"] = 1,
            ["Name"] = "RenamePremium",
            ["Timestamp"] = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act & Assert
        await Assert.That(() => BsonSerializer.Deserialize<MigrationRecord>(document)).ThrowsException();
    }
}
