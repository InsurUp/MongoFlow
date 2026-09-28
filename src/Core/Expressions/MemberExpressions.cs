using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

internal static class MemberExpressions
{
    /// <summary>Rewrites <c>(TSource x) =&gt; x.Member</c> as <c>(TTarget x) =&gt; ((TSource)x).Member</c>.</summary>
    public static Expression<Func<TTarget, TValue>> Rebind<TSource, TTarget, TValue>(Expression<Func<TSource, TValue>> expression)
    {
        if (expression is Expression<Func<TTarget, TValue>> same)
        {
            return same;
        }

        var parameter = Expression.Parameter(typeof(TTarget), expression.Parameters[0].Name);
        Expression source = typeof(TTarget) == typeof(TSource) ? parameter : Expression.Convert(parameter, typeof(TSource));

        var body = new ParameterReplacer(expression.Parameters[0], source).Visit(expression.Body);

        return Expression.Lambda<Func<TTarget, TValue>>(body, parameter);
    }

    /// <summary>Compiles <c>x =&gt; x.Member</c> into <c>(x, value) =&gt; x.Member = value</c>.</summary>
    /// <exception cref="ArgumentException">The body isn't a settable property or field of the parameter.</exception>
    public static Action<TSource, TValue> CreateSetter<TSource, TValue>(Expression<Func<TSource, TValue>> expression,
        string parameterName)
    {
        var member = GetMember(expression, parameterName);

        if (member.Member is not (PropertyInfo { CanWrite: true } or FieldInfo { IsInitOnly: false }))
        {
            throw new ArgumentException(
                $"Expected a settable property or field of the parameter, such as x => x.Member, but got {expression}.",
                parameterName);
        }

        var value = Expression.Parameter(typeof(TValue), "value");
        var assign = Expression.Assign(member, value.Type == member.Type ? value : Expression.Convert(value, member.Type));

        return Expression.Lambda<Action<TSource, TValue>>(assign, expression.Parameters[0], value).Compile();
    }

    /// <summary>The member <c>x =&gt; x.Member</c> reads, looking through a conversion of its result.</summary>
    /// <exception cref="ArgumentException">The body isn't a member of the parameter.</exception>
    public static MemberExpression GetMember(LambdaExpression expression, string parameterName)
    {
        var body = expression.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert
            ? convert.Operand
            : expression.Body;

        if (body is not MemberExpression member || member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException(
                $"Expected a property or field of the parameter, such as x => x.Member, but got {expression}.",
                parameterName);
        }

        return member;
    }
}

internal sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
}
