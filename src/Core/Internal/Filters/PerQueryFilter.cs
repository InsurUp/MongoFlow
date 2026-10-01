using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class PerQueryFilter<TDocument, TTarget>(
    Func<IServiceProvider, Expression<Func<TTarget, bool>>> filter,
    FeatureKey? owner) : QueryFilterEntry<TDocument>(owner)
{
    public override bool IsAsync => false;

    public override Expression<Func<TDocument, bool>> Resolve(IServiceProvider services) =>
        MemberExpressions.Rebind<TTarget, TDocument, bool>(filter(services));

    public override ValueTask<Expression<Func<TDocument, bool>>> ResolveAsync(IServiceProvider services,
        CancellationToken cancellationToken) => ValueTask.FromResult(Resolve(services));
}
