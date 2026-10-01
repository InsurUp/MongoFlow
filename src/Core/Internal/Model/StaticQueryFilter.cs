using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class StaticQueryFilter<TDocument, TTarget>(Expression<Func<TTarget, bool>> filter, FeatureKey? owner)
    : QueryFilterEntry<TDocument>(owner)
{
    private readonly Expression<Func<TDocument, bool>> _filter = MemberExpressions.Rebind<TTarget, TDocument, bool>(filter);

    public override bool IsAsync => false;

    public override Expression<Func<TDocument, bool>>? Static => _filter;

    public override Expression<Func<TDocument, bool>> Resolve(IServiceProvider services) => _filter;

    public override ValueTask<Expression<Func<TDocument, bool>>> ResolveAsync(IServiceProvider services,
        CancellationToken cancellationToken) => ValueTask.FromResult(_filter);
}
