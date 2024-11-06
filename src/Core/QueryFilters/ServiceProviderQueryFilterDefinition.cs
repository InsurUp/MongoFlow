using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class ServiceProviderQueryFilterDefinition : IQueryFilterDefinition
{
    private readonly Func<IServiceProvider, LambdaExpression> _expressionProvider;

    public ServiceProviderQueryFilterDefinition(Func<IServiceProvider, LambdaExpression> expressionProvider)
    {
        _expressionProvider = expressionProvider;
    }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expressionProvider(serviceProvider);
    }
}


internal sealed class ServiceProviderQueryFilterDefinition<TDocument> : IQueryFilterDefinition<TDocument>
{
    private readonly Func<IServiceProvider, Expression<Func<TDocument, bool>>> _expressionProvider;

    public ServiceProviderQueryFilterDefinition(Func<IServiceProvider, Expression<Func<TDocument, bool>>> expressionProvider)
    {
        _expressionProvider = expressionProvider;
    }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expressionProvider(serviceProvider);
    }
}
