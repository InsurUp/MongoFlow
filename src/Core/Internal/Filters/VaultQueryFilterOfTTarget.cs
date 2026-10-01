using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class VaultQueryFilter<TTarget> : VaultQueryFilter
{
    private readonly Expression<Func<TTarget, bool>>? _static;
    private readonly Func<IServiceProvider, Expression<Func<TTarget, bool>>>? _perQuery;
    private readonly Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>>? _async;

    public VaultQueryFilter(Layer layer, FeatureKey? owner,
        Expression<Func<TTarget, bool>>? @static = null,
        Func<IServiceProvider, Expression<Func<TTarget, bool>>>? perQuery = null,
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>>? async = null)
        : base(layer, owner)
    {
        _static = @static;
        _perQuery = perQuery;
        _async = async;
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
