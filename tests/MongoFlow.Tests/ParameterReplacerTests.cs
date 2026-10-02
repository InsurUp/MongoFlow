using System.Linq.Expressions;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="ParameterReplacer.Inline(System.Linq.Expressions.LambdaExpression, System.Linq.Expressions.Expression)"/>
/// puts an expression in place of a lambda's parameter, or two in place of its two, as if the lambda were called inline:
/// every use of those parameters is replaced, and nothing else.
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

    [Test]
    public async Task Inline_TwoArguments_PutsEachInPlaceOfItsParameter()
    {
        // Arrange
        Expression<Func<Basket, int, bool>> lambda = (x, limit) => x.Min > limit && x.Items.Length > limit;

        // Act
        var inlined = ParameterReplacer.Inline(lambda,
            Expression.Parameter(typeof(Basket), "y"),
            Expression.Constant(3));

        // Assert
        await Assert.That(inlined.ToString()).IsEqualTo("((y.Min > 3) AndAlso (ArrayLength(y.Items) > 3))");
    }

    /// <summary>A document with a member to compare and items to look into.</summary>
    public sealed class Basket
    {
        public int Min { get; set; }

        public int[] Items { get; set; } = [];
    }
}
