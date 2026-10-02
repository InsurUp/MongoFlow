namespace MongoFlow.Tests;

/// <summary>
/// <see cref="FeatureKey"/>: a key always has a name, compares by it, and prints as it, so a feature can be switched off
/// by a key built anywhere.
/// </summary>
public class FeatureKeyTests
{
    [Test]
    public async Task Constructor_Null_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new FeatureKey(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async Task Constructor_EmptyOrWhiteSpace_ThrowsArgumentException(string name)
    {
        // Act & Assert
        await Assert.That(() => new FeatureKey(name)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Equals_SameName_IsTrue()
    {
        // Arrange
        const string Name = "audit";

        // Act
        var equal = new FeatureKey(Name) == new FeatureKey(Name);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task ToString_AnyKey_ReturnsItsName()
    {
        // Arrange
        const string Name = "audit";

        // Act
        var text = new FeatureKey(Name).ToString();

        // Assert
        await Assert.That(text).IsEqualTo(Name);
    }
}
