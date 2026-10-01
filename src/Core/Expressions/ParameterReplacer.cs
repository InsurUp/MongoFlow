using System.Linq.Expressions;

namespace MongoFlow;

/// <summary>Puts an expression in place of a lambda's parameter, so lambdas can be joined or retyped into one plain lambda.</summary>
internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;
    private readonly Expression _replacement;

    private ParameterReplacer(ParameterExpression parameter,
        Expression replacement)
    {
        _parameter = parameter;
        _replacement = replacement;
    }

    /// <summary>The body of <c>x =&gt; body</c> with <paramref name="argument"/> in place of x, as if the lambda were called inline.</summary>
    public static Expression Inline(LambdaExpression lambda,
        Expression argument)
    {
        var parameter = lambda.Parameters[0];

        return parameter == argument ? lambda.Body : new ParameterReplacer(parameter, argument).Visit(lambda.Body);
    }

    protected override Expression VisitParameter(ParameterExpression node) => node == _parameter ? _replacement : node;
}
