using MongoDB.Bson;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="FilterDocuments.And"/> joins a write's key, its query filters and its condition into the filter the server
/// matches:
/// <list type="number">
/// <item>null filters drop out, nothing left means no filter, and a lone filter is returned as it is;</item>
/// <item>filters on different fields are joined side by side, which the server matches faster than <c>$and</c>;</item>
/// <item>filters that constrain the same field are joined with <c>$and</c>, so neither overrides the other;</item>
/// <item>the filters themselves are left unchanged, since a save shares its query filters between operations.</item>
/// </list>
/// </summary>
public class FilterDocumentsTests
{
    [Test]
    public async Task And_NoFilters_ReturnsNull()
    {
        // Act
        var joined = FilterDocuments.And(null, null);

        // Assert
        await Assert.That(joined).IsNull();
    }

    [Test]
    public async Task And_OneFilter_ReturnsItUnchanged()
    {
        // Arrange
        var key = new BsonDocument("_id", 1);

        // Act
        var joined = FilterDocuments.And(key, null);

        // Assert
        await Assert.That(joined).IsSameReferenceAs(key);
    }

    [Test]
    public async Task And_FiltersOnDifferentFields_JoinsThemSideBySide()
    {
        // Act
        var joined = FilterDocuments.And(
            new BsonDocument("_id", 1),
            new BsonDocument { { "IsDeleted", new BsonDocument("$ne", true) }, { "TenantId", "t-1" } },
            new BsonDocument("Version", 3));

        // Assert
        await Verify(joined);
    }

    [Test]
    public async Task And_FiltersOnTheSameField_JoinsThemWithAnd()
    {
        // Act
        var joined = FilterDocuments.And(
            new BsonDocument("_id", 1),
            null,
            new BsonDocument("_id", new BsonDocument("$ne", 2)));

        // Assert
        await Verify(joined);
    }

    [Test]
    public async Task And_AQueryFilterSharedBetweenWrites_IsLeftUnchanged()
    {
        // Arrange
        var queryFilter = new BsonDocument("IsDeleted", new BsonDocument("$ne", true));
        var before = queryFilter.ToJson();

        // Act
        FilterDocuments.And(new BsonDocument("_id", 1), queryFilter);
        FilterDocuments.And(new BsonDocument("_id", 2), queryFilter);

        // Assert
        await Assert.That(queryFilter.ToJson()).IsEqualTo(before);
    }
}
