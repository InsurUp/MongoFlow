using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

internal static class MemberExpressions
{
    /// <summary>Rewrites <c>(TSource x) =&gt; body</c> as <c>(TTarget x) =&gt; body</c>, with <c>(TSource)x</c> in place of x.</summary>
    /// <remarks>Not an extension: called as one, it would still need all three type arguments.</remarks>
    public static Expression<Func<TTarget, TValue>> Rebind<TSource, TTarget, TValue>(Expression<Func<TSource, TValue>> expression)
    {
        if (expression is Expression<Func<TTarget, TValue>> same)
        {
            return same;
        }

        var parameter = Expression.Parameter(typeof(TTarget), expression.Parameters[0].Name);
        var body = ParameterReplacer.Inline(expression, Expression.Convert(parameter, typeof(TSource)));

        return Expression.Lambda<Func<TTarget, TValue>>(body, parameter);
    }

    extension(Expression expression)
    {
        /// <summary>
        /// The member of <paramref name="parameter"/> this reads, such as <c>x.Member</c>, looking through a conversion of
        /// the result; otherwise <see langword="null"/>.
        /// </summary>
        public MemberExpression? AsMemberOf(ParameterExpression parameter) =>
            (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : expression)
                is MemberExpression member && member.Expression == parameter
                ? member
                : null;
    }

    extension(LambdaExpression expression)
    {
        /// <summary>The member <c>x =&gt; x.Member</c> reads, looking through a conversion of its result.</summary>
        /// <exception cref="ArgumentException">The body isn't a member of the parameter.</exception>
        public MemberExpression GetMember(string parameterName) =>
            expression.Body.AsMemberOf(expression.Parameters[0])
            ?? throw new ArgumentException(
                $"Expected a property or field of the parameter, such as x => x.Member, but got {expression}.",
                parameterName);
    }

    extension<TSource, TValue>(Expression<Func<TSource, TValue>> expression)
    {
        /// <summary>Compiles <c>x =&gt; x.Member</c> into <c>(x, value) =&gt; x.Member = value</c>.</summary>
        /// <exception cref="ArgumentException">The body isn't a settable property or field of the parameter.</exception>
        public Action<TSource, TValue> CreateSetter(string parameterName)
        {
            var member = expression.GetMember(parameterName);

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
    }
}
