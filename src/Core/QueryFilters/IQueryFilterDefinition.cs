using System.Linq.Expressions;

namespace MongoFlow;

public interface IQueryFilterDefinition
{
    LambdaExpression Get(IServiceProvider serviceProvider);
}

public interface IQueryFilterDefinition<TDocument> : IQueryFilterDefinition;
