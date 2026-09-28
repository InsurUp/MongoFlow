using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace MongoFlow;

internal static class KeyFilterExpressionBuilder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Expression<Func<TDocument, bool>> CreateKeyFilter<TDocument>(
        object? key,
        Expression<Func<TDocument, object>> keyExpression)
    {
        if (key is null)
        {
            return _ => false;
        }

        var propertyExpression = keyExpression.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : keyExpression.Body;

        var convertedKey = propertyExpression.Type == key.GetType()
            ? key
            : throw new InvalidCastException();

        return Expression.Lambda<Func<TDocument, bool>>(
            Expression.Equal(
                propertyExpression,
                Expression.Constant(convertedKey, propertyExpression.Type)
            ),
            keyExpression.Parameters[0]
        );
    }
}
