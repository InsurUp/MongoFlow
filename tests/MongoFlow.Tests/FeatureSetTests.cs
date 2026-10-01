namespace MongoFlow.Tests;

/// <summary>
/// <see cref="FeatureSet"/>, the features switched off on a collection view, keys the per-save query filter cache:
/// <list type="number">
/// <item>sets with the same features are equal and hash alike, whatever order they were switched off in;</item>
/// <item>switching off a feature that is already off returns the same set;</item>
/// <item>a default key is rejected.</item>
/// </list>
/// </summary>
public class FeatureSetTests
{
    private static readonly FeatureKey Audit = new("audit");
    private static readonly FeatureKey Tenancy = new("tenancy");

    [Test]
    public async Task Empty_AnyFeature_IsNotContained()
    {
        // Act
        var set = FeatureSet.Empty;

        // Assert
        await Verify(new { set.IsEmpty, Audit = set.Contains(Audit) });
    }

    [Test]
    public async Task With_AFeature_ContainsOnlyIt()
    {
        // Act
        var set = FeatureSet.Empty.With(Audit);

        // Assert
        await Verify(new { set.IsEmpty, Audit = set.Contains(Audit), Tenancy = set.Contains(Tenancy) });
    }

    [Test]
    public async Task With_AFeatureAlreadyOff_ReturnsTheSameSet()
    {
        // Arrange
        var set = FeatureSet.Empty.With(Audit);

        // Act
        var again = set.With(Audit);

        // Assert
        await Assert.That(again).IsSameReferenceAs(set);
    }

    [Test]
    public async Task With_DefaultKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.That(() => FeatureSet.Empty.With(default)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Equals_SameFeaturesInAnotherOrder_IsTrueWithTheSameHash()
    {
        // Arrange
        var first = FeatureSet.Empty.With(Audit).With(Tenancy);
        var second = FeatureSet.Empty.With(Tenancy).With(Audit);

        // Act
        var equal = first.Equals(second);

        // Assert
        await Assert.That(equal).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Equals_OtherFeatures_IsFalse()
    {
        // Arrange
        var audit = FeatureSet.Empty.With(Audit);
        var tenancy = FeatureSet.Empty.With(Tenancy);

        // Act
        var equal = audit.Equals(tenancy);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_Itself_IsTrue()
    {
        // Arrange
        var set = FeatureSet.Empty.With(Audit);

        // Act
        var equal = set.Equals(set);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task Equals_Null_IsFalse()
    {
        // Act
        var equal = FeatureSet.Empty.Equals(null);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task EqualsObject_AnotherType_IsFalse()
    {
        // Act
        var equal = FeatureSet.Empty.Equals((object)Audit);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task EqualsObject_AnEqualSet_IsTrue()
    {
        // Act
        var equal = FeatureSet.Empty.With(Audit).Equals((object)FeatureSet.Empty.With(Audit));

        // Assert
        await Assert.That(equal).IsTrue();
    }
}
