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
