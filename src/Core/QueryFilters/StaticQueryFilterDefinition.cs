using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class StaticQueryFilterDefinition : IQueryFilterDefinition
{
    private readonly LambdaExpression _expression;

    public StaticQueryFilterDefinition(LambdaExpression expression)
    {
        _expression = expression;
    }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expression;
    }
}

internal sealed class StaticQueryFilterDefinition<TDocument> : IQueryFilterDefinition<TDocument>
{
    private readonly Expression<Func<TDocument, bool>> _expression;

    public StaticQueryFilterDefinition(Expression<Func<TDocument, bool>> expression)
    {
        _expression = expression;
    }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expression;
    }
}
