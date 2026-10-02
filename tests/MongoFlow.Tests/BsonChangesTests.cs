namespace MongoFlow.Tests;

/// <summary>
/// <see cref="BsonChanges.Touches"/> tells whether a change reaches a key's fields: the field itself, a field inside it,
/// or a document holding it, and not a sibling whose name starts the same.
/// </summary>
public class BsonChangesTests
{
    [Test]
    [Arguments("_id", "_id", true)]
    [Arguments("_id.Number", "_id", true)]
    [Arguments("Info", "Info.Number", true)]
    [Arguments("_idx", "_id", false)]
    [Arguments("Info.Numbers", "Info.Number", false)]
    [Arguments("_ix.Number", "_id", false)]
    [Arguments("Customer", "_id", false)]
    public async Task Touches_ChangedPathAndKeyField_ReachesItOrNot(string path,
        string field,
        bool reaches)
    {
        // Arrange
        var changes = new BsonChanges();
        changes.Set(path);

        // Act
        var touches = changes.Touches([field]);

        // Assert
        await Assert.That(touches).IsEqualTo(reaches);
    }
}
