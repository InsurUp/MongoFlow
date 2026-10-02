using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// The driver's find, tracking the documents it returns. Sorting, skipping and limiting change the driver's find in place,
/// as the driver does; a projection is the driver's own, untracked.
/// </summary>
internal sealed class TrackingFindFluent<TDocument>(IFindFluent<TDocument, TDocument> find,
    IDocumentTracker<TDocument> tracker) : IOrderedFindFluent<TDocument, TDocument>
{
    public FilterDefinition<TDocument> Filter
    {
        get => find.Filter;
        set => find.Filter = value;
    }

    public FindOptions<TDocument, TDocument> Options => find.Options;

    public IFindFluent<TDocument, TResult> As<TResult>(IBsonSerializer<TResult>? resultSerializer = null) =>
        find.As(resultSerializer);

    [Obsolete("Use CountDocuments instead.")]
    public long Count(CancellationToken cancellationToken = default) => find.Count(cancellationToken);

    [Obsolete("Use CountDocumentsAsync instead.")]
    public Task<long> CountAsync(CancellationToken cancellationToken = default) => find.CountAsync(cancellationToken);

    public long CountDocuments(CancellationToken cancellationToken = default) => find.CountDocuments(cancellationToken);

    public Task<long> CountDocumentsAsync(CancellationToken cancellationToken = default) =>
        find.CountDocumentsAsync(cancellationToken);

    public IFindFluent<TDocument, TDocument> Limit(int? limit)
    {
        find.Limit(limit);
        return this;
    }

    public IFindFluent<TDocument, TNewProjection> Project<TNewProjection>(ProjectionDefinition<TDocument, TNewProjection> projection) =>
        find.Project(projection);

    public IFindFluent<TDocument, TDocument> Skip(int? skip)
    {
        find.Skip(skip);
        return this;
    }

    public IFindFluent<TDocument, TDocument> Sort(SortDefinition<TDocument> sort)
    {
        find.Sort(sort);
        return this;
    }

    public IAsyncCursor<TDocument> ToCursor(CancellationToken cancellationToken = default) =>
        Tracked(find.ToCursor(cancellationToken));

    public async Task<IAsyncCursor<TDocument>> ToCursorAsync(CancellationToken cancellationToken = default) =>
        Tracked(await find.ToCursorAsync(cancellationToken));

    public string ToString(ExpressionTranslationOptions translationOptions) => find.ToString(translationOptions);

    public override string ToString() => find.ToString()!;

    // A projection set on the options directly returns partial documents too.
    private IAsyncCursor<TDocument> Tracked(IAsyncCursor<TDocument> cursor) =>
        find.Options.Projection is null ? new TrackingCursor<TDocument, TDocument>(cursor, tracker) : cursor;
}
