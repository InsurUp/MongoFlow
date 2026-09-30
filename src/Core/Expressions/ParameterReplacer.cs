using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
}
