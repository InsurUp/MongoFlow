using System.Linq.Expressions;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="ParameterReplacer.Inline"/> puts an expression in place of a lambda's parameter, as if the lambda were
/// called inline: every use of that parameter is replaced, and nothing else.
/// </summary>
public partial class ParameterReplacerTests
{
    [Test]
    public async Task Inline_TheLambdasOwnParameter_ReturnsItsBody()
    {
        // Arrange
        Expression<Func<Basket, bool>> lambda = x => x.Min > 1;

        // Act
        var inlined = ParameterReplacer.Inline(lambda, lambda.Parameters[0]);

        // Assert
        await Assert.That(inlined).IsSameReferenceAs(lambda.Body);
    }

    [Test]
    public async Task Inline_AnotherExpression_PutsItInPlaceOfEveryUse()
    {
        // Arrange
        Expression<Func<Basket, bool>> lambda = x => x.Min > 1 && x.Items.Length > x.Min;

        // Act
        var inlined = ParameterReplacer.Inline(lambda, Expression.Parameter(typeof(Basket), "y"));

        // Assert
        await Assert.That(inlined.ToString()).IsEqualTo("((y.Min > 1) AndAlso (ArrayLength(y.Items) > y.Min))");
    }

    [Test]
    public async Task Inline_ANestedLambda_LeavesItsParameterAlone()
    {
        // Arrange
        Expression<Func<Basket, bool>> lambda = x => x.Items.Any(item => item > x.Min);

        // Act
        var inlined = ParameterReplacer.Inline(lambda, Expression.Parameter(typeof(Basket), "y"));

        // Assert
        await Assert.That(inlined.ToString()).IsEqualTo("y.Items.Any(item => (item > y.Min))");
    }

    /// <summary>A document with a member to compare and items to look into.</summary>
    public sealed class Basket
    {
        public int Min { get; set; }

        public int[] Items { get; set; } = [];
    }
}
