using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

internal class VaultCollection<TDocument>(
    VaultRuntime runtime,
    CollectionModel<TDocument> model,
    IReadOnlySet<FeatureKey> disabled) : IVaultCollection<TDocument>
{
    protected VaultRuntime Runtime { get; } = runtime;

    protected IReadOnlySet<FeatureKey> Disabled { get; } = disabled;

    public IMongoCollection<TDocument> MongoCollection => model.MongoCollection;

    public virtual IVaultCollection<TDocument> Without(FeatureKey feature) =>
        new VaultCollection<TDocument>(Runtime, model, With(Disabled, feature));

    public async ValueTask<IQueryable<TDocument>> QueryAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        var queryable = session is null ? model.MongoCollection.AsQueryable() : model.MongoCollection.AsQueryable(session);

        return filter is null ? queryable : queryable.Where(filter);
    }

    public async ValueTask<IFindFluent<TDocument, TDocument>> FindAsync(Expression<Func<TDocument, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var combined = FilterExpressions.Combine(filter, await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken));
        FilterDefinition<TDocument> definition = combined is null ? FilterDefinition<TDocument>.Empty : combined;

        var session = await Runtime.GetSessionAsync(cancellationToken);

        return session is null ? model.MongoCollection.Find(definition) : model.MongoCollection.Find(session, definition);
    }

    public async ValueTask<IAggregateFluent<TDocument>> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var filter = await model.ResolveFilterAsync(Runtime.Services, Disabled, cancellationToken);
        var session = await Runtime.GetSessionAsync(cancellationToken);

        var aggregate = session is null ? model.MongoCollection.Aggregate() : model.MongoCollection.Aggregate(session);

        return filter is null ? aggregate : aggregate.Match(filter);
    }

    public void Add(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Runtime.Enqueue(new InsertOperation<TDocument>(model, Disabled, document));
    }

    public void AddRange(IEnumerable<TDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            Add(document);
        }
    }

    public void UpdateMany(Expression<Func<TDocument, bool>> filter, UpdateDefinition<TDocument> update)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);

        Runtime.Enqueue(new UpdateOperation<TDocument>(model, Disabled, null, filter, update, default));
    }

    public void DeleteMany(Expression<Func<TDocument, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        Runtime.Enqueue(new DeleteOperation<TDocument>(model, Disabled, null, filter, default));
    }

    protected static IReadOnlySet<FeatureKey> With(IReadOnlySet<FeatureKey> disabled, FeatureKey feature)
    {
        FeatureKeys.ThrowIfDefault(feature, nameof(feature));

        return new HashSet<FeatureKey>(disabled) { feature };
    }
}
