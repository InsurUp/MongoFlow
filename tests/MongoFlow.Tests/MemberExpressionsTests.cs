using System.Linq.Expressions;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="MemberExpressions"/> reads and writes the members features are configured with:
/// <list type="number">
/// <item><c>Rebind</c> retypes a filter written against an interface onto a document type, and leaves one already of
/// that type alone;</item>
/// <item><c>AsMemberOf</c> and <c>GetMember</c> find the member <c>x =&gt; x.Member</c> reads, through a conversion of
/// its result, and nothing else;</item>
/// <item><c>CreateSetter</c> compiles a setter for a settable property or field, converting the value to the member's
/// type, and rejects anything else.</item>
/// </list>
/// </summary>
public partial class MemberExpressionsTests
{
    [Test]
    public async Task Rebind_SameType_ReturnsTheExpression()
    {
        // Arrange
        Expression<Func<Order, bool>> filter = x => !x.IsDeleted;

        // Act
        var rebound = MemberExpressions.Rebind<Order, Order, bool>(filter);

        // Assert
        await Assert.That(rebound).IsSameReferenceAs(filter);
    }

    [Test]
    public async Task Rebind_ToAnImplementingType_ConvertsTheParameter()
    {
        // Arrange
        Expression<Func<ISoftDeletable, bool>> filter = x => !x.IsDeleted;

        // Act
        var rebound = MemberExpressions.Rebind<ISoftDeletable, Order, bool>(filter);

        // Assert
        await Assert.That(rebound.ToString()).IsEqualTo("x => Not(Convert(x, ISoftDeletable).IsDeleted)");
    }

    [Test]
    public async Task AsMemberOf_AMemberOfTheParameter_ReturnsIt()
    {
        // Arrange
        Expression<Func<Counter, int>> lambda = x => x.Count;

        // Act
        var member = lambda.Body.AsMemberOf(lambda.Parameters[0]);

        // Assert
        await Assert.That(member).IsSameReferenceAs(lambda.Body);
    }

    [Test]
    public async Task AsMemberOf_AConvertedMember_ReturnsTheMember()
    {
        // Arrange
        Expression<Func<Counter, long>> lambda = x => x.Count;

        // Act
        var member = lambda.Body.AsMemberOf(lambda.Parameters[0]);

        // Assert
        await Assert.That(member!.ToString()).IsEqualTo("x.Count");
    }

    [Test]
    public async Task AsMemberOf_AMemberOfAMember_ReturnsNull()
    {
        // Arrange
        Expression<Func<Counter, int>> lambda = x => x.Name.Length;

        // Act
        var member = lambda.Body.AsMemberOf(lambda.Parameters[0]);

        // Assert
        await Assert.That(member).IsNull();
    }

    [Test]
    public async Task AsMemberOf_NotAMember_ReturnsNull()
    {
        // Arrange
        Expression<Func<Counter, int>> lambda = x => x.Count + 1;

        // Act
        var member = lambda.Body.AsMemberOf(lambda.Parameters[0]);

        // Assert
        await Assert.That(member).IsNull();
    }

    [Test]
    public async Task GetMember_NotAMember_ThrowsArgumentException()
    {
        // Arrange
        Expression<Func<Counter, int>> lambda = x => x.Count + 1;

        // Act & Assert
        await Throws(() => lambda.GetMember("key")).IgnoreStackTrace();
    }

    [Test]
    public async Task CreateSetter_Property_SetsIt()
    {
        // Arrange
        const int Count = 3;
        Expression<Func<Counter, int>> member = x => x.Count;
        var counter = new Counter();

        // Act
        member.CreateSetter("member")(counter, Count);

        // Assert
        await Assert.That(counter.Count).IsEqualTo(Count);
    }

    [Test]
    public async Task CreateSetter_Field_SetsIt()
    {
        // Arrange
        const string Label = "first";
        Expression<Func<Counter, string>> member = x => x.Label;
        var counter = new Counter();

        // Act
        member.CreateSetter("member")(counter, Label);

        // Assert
        await Assert.That(counter.Label).IsEqualTo(Label);
    }

    [Test]
    public async Task CreateSetter_ConvertedMember_ConvertsTheValue()
    {
        // Arrange
        const int Count = 3;
        Expression<Func<Counter, int?>> member = x => x.Count;
        var counter = new Counter();

        // Act
        member.CreateSetter("member")(counter, Count);

        // Assert
        await Assert.That(counter.Count).IsEqualTo(Count);
    }

    [Test]
    public async Task CreateSetter_ReadOnlyProperty_ThrowsArgumentException()
    {
        // Arrange
        Expression<Func<Counter, string>> member = x => x.Name;

        // Act & Assert
        await Throws(() => member.CreateSetter("member")).IgnoreStackTrace();
    }

    [Test]
    public async Task CreateSetter_ReadOnlyField_ThrowsArgumentException()
    {
        // Arrange
        Expression<Func<Counter, int>> member = x => x.Limit;

        // Act & Assert
        await Assert.That(() => member.CreateSetter("member")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task CreateSetter_NotAMember_ThrowsArgumentException()
    {
        // Arrange
        Expression<Func<Counter, int>> member = x => x.Count + 1;

        // Act & Assert
        await Assert.That(() => member.CreateSetter("member")).ThrowsExactly<ArgumentException>();
    }

    /// <summary>A document with each kind of member a feature can be configured with.</summary>
    public sealed class Counter
    {
        public readonly int Limit = 10;

        public string Label = "";

        public int Count { get; set; }

        public string Name => Label;
    }
}
