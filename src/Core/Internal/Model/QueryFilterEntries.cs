using System.Linq.Expressions;

namespace MongoFlow;

/// <summary>One query filter of a collection, written against <typeparamref name="TDocument"/> or a type it's assignable to.</summary>
internal abstract class QueryFilterEntry<TDocument>(FeatureKey? owner)
{
    /// <summary>The feature the filter belongs to, or <see langword="null"/> when it's always on.</summary>
    public FeatureKey? Owner { get; } = owner;

    public abstract bool IsAsync { get; }

    /// <summary>Resolves a filter that isn't asynchronous.</summary>
    public abstract Expression<Func<TDocument, bool>> Resolve(IServiceProvider services);

    public abstract ValueTask<Expression<Func<TDocument, bool>>> ResolveAsync(IServiceProvider services,
        CancellationToken cancellationToken);
}

internal sealed class StaticQueryFilter<TDocument, TTarget>(Expression<Func<TTarget, bool>> filter, FeatureKey? owner)
    : QueryFilterEntry<TDocument>(owner)
{
    private readonly Expression<Func<TDocument, bool>> _filter = MemberExpressions.Rebind<TTarget, TDocument, bool>(filter);

    public override bool IsAsync => false;

    public override Expression<Func<TDocument, bool>> Resolve(IServiceProvider services) => _filter;

    public override ValueTask<Expression<Func<TDocument, bool>>> ResolveAsync(IServiceProvider services,
        CancellationToken cancellationToken) => ValueTask.FromResult(_filter);
}

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

/// <summary>A filter added on the vault, applied to every collection whose document is assignable to its target.</summary>
internal abstract class VaultQueryFilter(Layer layer, FeatureKey? owner)
{
    public Layer Layer { get; } = layer;

    public FeatureKey? Owner { get; } = owner;

    public abstract QueryFilterEntry<TDocument>? For<TDocument>();
}

internal sealed class VaultQueryFilter<TTarget> : VaultQueryFilter
{
    private readonly Expression<Func<TTarget, bool>>? _static;
    private readonly Func<IServiceProvider, Expression<Func<TTarget, bool>>>? _perQuery;
    private readonly Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>>? _async;

    public VaultQueryFilter(Layer layer, FeatureKey? owner,
        Expression<Func<TTarget, bool>>? @static = null,
        Func<IServiceProvider, Expression<Func<TTarget, bool>>>? perQuery = null,
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>>? @async = null)
        : base(layer, owner)
    {
        _static = @static;
        _perQuery = perQuery;
        _async = @async;
    }

    public override QueryFilterEntry<TDocument>? For<TDocument>()
    {
        if (!typeof(TDocument).IsAssignableTo(typeof(TTarget)))
        {
            return null;
        }

        if (_static is not null)
        {
            return new StaticQueryFilter<TDocument, TTarget>(_static, Owner);
        }

        return _perQuery is not null
            ? new PerQueryFilter<TDocument, TTarget>(_perQuery, Owner)
            : new AsyncQueryFilter<TDocument, TTarget>(_async!, Owner);
    }
}
