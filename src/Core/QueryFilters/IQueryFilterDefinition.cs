using System.Linq.Expressions;

namespace MongoFlow;

public interface IQueryFilterDefinition
{
    string? Name { get; }
    LambdaExpression Get(IServiceProvider serviceProvider);
}

public interface IQueryFilterDefinition<TDocument> : IQueryFilterDefinition;
