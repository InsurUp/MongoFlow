using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class ServiceProviderQueryFilterDefinition : IQueryFilterDefinition
{
    private readonly Func<IServiceProvider, LambdaExpression> _expressionProvider;

    public ServiceProviderQueryFilterDefinition(Func<IServiceProvider, LambdaExpression> expressionProvider,
        string? name)
    {
        _expressionProvider = expressionProvider;
        Name = name;
    }

    public string? Name { get; }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expressionProvider(serviceProvider);
    }
}


internal sealed class ServiceProviderQueryFilterDefinition<TDocument> : IQueryFilterDefinition<TDocument>
{
    private readonly Func<IServiceProvider, Expression<Func<TDocument, bool>>> _expressionProvider;

    public ServiceProviderQueryFilterDefinition(Func<IServiceProvider, Expression<Func<TDocument, bool>>> expressionProvider,
        string? name)
    {
        _expressionProvider = expressionProvider;
        Name = name;
    }

    public string? Name { get; }

    public LambdaExpression Get(IServiceProvider serviceProvider)
    {
        return _expressionProvider(serviceProvider);
    }
}
