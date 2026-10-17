using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>An index a collection declares, rendered with the collection's serializers: what the migrator creates.</summary>
internal sealed record IndexDeclaration(string Name,
    BsonDocument Keys,
    bool Unique,
    bool Sparse,
    BsonDocument? Partial,
    TimeSpan? ExpireAfter,
    bool Hidden,
    Collation? Collation)
{
    /// <summary>
    /// The index as <c>listIndexes</c> lists it, with text keys as <c>_fts</c> and <c>_ftsx</c> and their fields as
    /// weights: what the index check reads.
    /// </summary>
    public BsonDocument Listed => field ??= IndexSpecs.Listed(this);

    /// <summary>What creates it.</summary>
    public CreateIndexModel<BsonDocument> Model() =>
        new(new BsonDocumentIndexKeysDefinition<BsonDocument>(Keys),
            new CreateIndexOptions<BsonDocument>
            {
                Name = Name,
                Unique = Unique ? true : null,
                Sparse = Sparse ? true : null,
                PartialFilterExpression = Partial is null ? null : new BsonDocumentFilterDefinition<BsonDocument>(Partial),
                ExpireAfter = ExpireAfter,
                Hidden = Hidden ? true : null,
                Collation = Collation
            });

    /// <summary>How it's shown in logs: its keys, then its options, such as <c>{ "Customer" : 1 }, unique</c>.</summary>
    public string Definition =>
        string.Join(", ", new[]
        {
            Keys.ToJson(),
            Unique ? "unique" : null,
            Sparse ? "sparse" : null,
            Partial is null ? null : $"partial {Partial.ToJson()}",
            ExpireAfter is { } expiry ? $"expiring after {expiry.TotalSeconds:0} s" : null,
            Hidden ? "hidden" : null,
            Collation is null ? null : $"collation {StoredSpecs.Describe(Collation.ToBsonDocument())}"
        }.OfType<string>());
}
