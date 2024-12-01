using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using ExpressionVisitor = System.Linq.Expressions.ExpressionVisitor;

namespace MongoFlow;

internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly Expression _old;
    private readonly Expression _new;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ParameterReplacer(Expression oldParam, Expression newParam)
    {
        _old = oldParam;
        _new = newParam;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Expression? Visit(Expression? node)
    {
        if (node is null) return null;
        return ReferenceEquals(node, _old) ? _new : base.Visit(node);
    }
}
