using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

public sealed class DocumentSetConfigurationBuilder<TDocument> : DocumentSetConfigurationBuilder
{
    internal DocumentSetConfigurationBuilder(InternalDocumentSetConfigurationBuilder builder) : base(builder)
    {
    }

    public void Key(Expression<Func<TDocument, object>> key)
    {
        var property = (PropertyInfo)((MemberExpression)key.Body).Member;

        Key(property);
    }

    public void AddQueryFilter(Expression<Func<TDocument, bool>> expression)
    {
        AddQueryFilter(null, expression);
    }
    
    public void AddQueryFilter(string? name, Expression<Func<TDocument, bool>> expression)
    {
        AddQueryFilter(new StaticQueryFilterDefinition<TDocument>(expression, name));
    }

    public void AddQueryFilter(Func<IServiceProvider, Expression<Func<TDocument, bool>>> expression)
    {
        AddQueryFilter(null, expression);
    }
    
    public void AddQueryFilter(string? name, Func<IServiceProvider, Expression<Func<TDocument, bool>>> expression)
    {
        AddQueryFilter(new ServiceProviderQueryFilterDefinition<TDocument>(expression, name));
    }
}

public class DocumentSetConfigurationBuilder
{
    internal DocumentSetConfigurationBuilder(InternalDocumentSetConfigurationBuilder builder)
    {
        Builder = builder;
    }

    internal InternalDocumentSetConfigurationBuilder Builder { get; }

    public void Key(DocumentProperty property)
    {
        Builder.Key(property);
    }

    public void Key(string name)
    {
        Builder.Key(name);
    }

    public void Key(PropertyInfo propertyInfo)
    {
        Builder.Key(propertyInfo);
    }

    public void Name(string name)
    {
        Builder.Name(name);
    }

    internal void AddQueryFilter(IQueryFilterDefinition queryFilterDefinition)
    {
        Builder.AddQueryFilter(queryFilterDefinition);
    }

    public void AddQueryFilter(LambdaExpression expression)
    {
        AddQueryFilter(null, expression);
    }
    
    public void AddQueryFilter(string? name, LambdaExpression expression)
    {
        Builder.AddQueryFilter(name, expression);
    }

    public void AddQueryFilter(Func<IServiceProvider, LambdaExpression> expressionProvider)
    {
        AddQueryFilter(null, expressionProvider);
    }
    
    public void AddQueryFilter(string? name, Func<IServiceProvider, LambdaExpression> expressionProvider)
    {
        Builder.AddQueryFilter(name, expressionProvider);
    }
}
