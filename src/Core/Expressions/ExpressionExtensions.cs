using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace MongoFlow;

internal static class ExpressionExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Expression<Func<T, bool>> CombineAnd<T>(this LambdaExpression[] expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);

        return expressions.Length switch
        {
            0 => DefaultTrue<T>(),
            1 => ConvertSingle<T>(expressions[0]),
            2 => CombineTwo<T>(expressions[0], expressions[1]),
            _ => CombineMultiple<T>(expressions)
        };
    }

    private static Expression<Func<T, bool>> DefaultTrue<T>() => _ => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Expression<Func<T, bool>> ConvertSingle<T>(LambdaExpression expression)
    {
        if (expression is Expression<Func<T, bool>> typed)
            return typed;

        var parameter = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(
            CreateBody(expression, parameter),
            parameter
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Expression<Func<T, bool>> CombineTwo<T>(LambdaExpression first, LambdaExpression second)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(
                CreateBody(first, parameter),
                CreateBody(second, parameter)
            ),
            parameter
        );
    }

    private static Expression<Func<T, bool>> CombineMultiple<T>(LambdaExpression[] expressions)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var bodies = new Expression[expressions.Length];

        for (int i = 0; i < expressions.Length; i++)
        {
            bodies[i] = CreateBody(expressions[i], parameter);
        }

        return Expression.Lambda<Func<T, bool>>(
            CombineExpressions(bodies),
            parameter
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Expression CreateBody(LambdaExpression expression, ParameterExpression newParameter)
    {
        var originalParam = expression.Parameters[0];
        if (originalParam.Type == newParameter.Type)
        {
            return SubstituteParameter(expression.Body, originalParam, newParameter);
        }

        return SubstituteParameter(
            expression.Body,
            originalParam,
            Expression.Convert(newParameter, originalParam.Type)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Expression SubstituteParameter(Expression body, Expression oldParam, Expression newParam)
    {
        if (body is ParameterExpression parameter && parameter == oldParam)
            return newParam;

        // Handle common expression types directly to avoid visitor overhead
        switch (body.NodeType)
        {
            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
                var binary = (BinaryExpression)body;
                var left = SubstituteParameter(binary.Left, oldParam, newParam);
                var right = SubstituteParameter(binary.Right, oldParam, newParam);
                return left == binary.Left && right == binary.Right
                    ? body
                    : Expression.MakeBinary(body.NodeType, left, right);

            case ExpressionType.MemberAccess:
                var member = (MemberExpression)body;
                var expr = SubstituteParameter(member.Expression!, oldParam, newParam);
                return expr == member.Expression
                    ? body
                    : Expression.MakeMemberAccess(expr, member.Member);

            default:
                // Fallback to visitor for complex cases
                return new ParameterReplacer(oldParam, newParam).Visit(body)!;
        }
    }

    private static Expression CombineExpressions(Expression[] expressions)
    {
        // Use balanced tree algorithm for better performance
        return CombineRange(expressions, 0, expressions.Length - 1);
    }

    private static Expression CombineRange(Expression[] expressions, int start, int end)
    {
        if (start == end) return expressions[start];
        if (end - start == 1) return Expression.AndAlso(expressions[start], expressions[end]);

        var mid = (start + end) / 2;
        return Expression.AndAlso(
            CombineRange(expressions, start, mid),
            CombineRange(expressions, mid + 1, end)
        );
    }
    
    public static Expression<Func<T, bool>> CombineOr<T>(this Expression<Func<T, bool>>[] expressions)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var combined = expressions
            .Select(expr => 
            {
                var visitor = new ParameterReplacer(expr.Parameters[0], parameter);
                return visitor.Visit(expr.Body)!;
            })
            .Aggregate(Expression.OrElse);

        return Expression.Lambda<Func<T, bool>>(combined, parameter);
    }
}
