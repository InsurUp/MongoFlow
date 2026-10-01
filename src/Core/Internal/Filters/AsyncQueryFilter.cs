using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class AsyncQueryFilter<TDocument, TTarget>(
    Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>> filter,
    FeatureKey? owner) : QueryFilterEntry<TDocument>(owner)
{
    public override bool IsAsync => true;

    public override Expression<Func<TDocument, bool>> Resolve(IServiceProvider services) =>
        throw new InvalidOperationException("An asynchronous query filter has to be resolved asynchronously.");

    public override async ValueTask<Expression<Func<TDocument, bool>>> ResolveAsync(IServiceProvider services,
        CancellationToken cancellationToken) =>
        MemberExpressions.Rebind<TTarget, TDocument, bool>(await filter(services, cancellationToken));
}
