using System.Linq.Expressions;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="FilterExpressions.Combine{T}"/> joins query filters into the one filter a read or write applies:
/// <list type="number">
/// <item>null filters and the constant <see langword="true"/> drop out, and nothing left means no filter;</item>
/// <item>a lone filter is returned as it is, so a static filter isn't rebuilt;</item>
/// <item>the constant <see langword="false"/> is the result, matching nothing;</item>
/// <item>the rest are joined with AND onto the first filter's parameter.</item>
/// </list>
/// </summary>
public class FilterExpressionsTests
{
    [Test]
    public async Task Combine_NoFilters_ReturnsNull()
    {
        // Act
        var combined = FilterExpressions.Combine<Order>();

        // Assert
        await Assert.That(combined).IsNull();
    }

    [Test]
    public async Task Combine_NullAndConstantTrue_ReturnsNull()
    {
        // Act
        var combined = FilterExpressions.Combine<Order>(null, _ => true);

        // Assert
        await Assert.That(combined).IsNull();
    }

    [Test]
    public async Task Combine_OneFilterAmongNullAndConstantTrue_ReturnsItUnchanged()
    {
        // Arrange
        Expression<Func<Order, bool>> filter = x => x.Total > 10;

        // Act
        var combined = FilterExpressions.Combine(null, filter, _ => true);

        // Assert
        await Assert.That(combined).IsSameReferenceAs(filter);
    }

    [Test]
    public async Task Combine_ConstantFalse_ReturnsIt()
    {
        // Arrange
        Expression<Func<Order, bool>> nothing = _ => false;

        // Act
        var combined = FilterExpressions.Combine(x => x.Total > 10, nothing, x => x.Customer == "ada");

        // Assert
        await Assert.That(combined).IsSameReferenceAs(nothing);
    }

    [Test]
    public async Task Combine_SeveralFilters_JoinsThemOnTheFirstParameter()
    {
        // Arrange
        Expression<Func<Order, bool>> first = x => x.Total > 10;

        // Act
        var combined = FilterExpressions.Combine(first, y => y.Customer == "ada", z => !z.IsDeleted);

        // Assert
        await Assert.That(combined!.Parameters[0]).IsSameReferenceAs(first.Parameters[0]);
        await Assert.That(combined.ToString()).IsEqualTo("x => (((x.Total > 10) AndAlso (x.Customer == \"ada\")) AndAlso Not(x.IsDeleted))");
    }
}
