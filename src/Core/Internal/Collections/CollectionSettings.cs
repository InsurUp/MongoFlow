using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// What a collection declares it's created with, rendered with its serializers. A setting left out is
/// <see langword="null"/>, or <see langword="false"/> for clustering, and isn't compared.
/// </summary>
internal sealed record CollectionSettings(Collation? Collation,
    BsonDocument? Validator,
    DocumentValidationLevel ValidationLevel,
    DocumentValidationAction ValidationAction,
    bool? ChangeStreamPreAndPostImages,
    bool Clustered,
    TimeSpan? ExpireAfter)
{
    /// <summary>
    /// What creates the collection, encrypted with <paramref name="encryptedFields"/> if given, and without the validator
    /// when <paramref name="validator"/> is <see langword="false"/>, for documents copied in before it applies.
    /// </summary>
    public CreateCollectionOptions<BsonDocument> CreateOptions(BsonDocument? encryptedFields = null,
        bool validator = true)
    {
        var validates = validator && Validator is not null;

        return new CreateCollectionOptions<BsonDocument>
        {
            Collation = Collation,
            Validator = validates ? new BsonDocumentFilterDefinition<BsonDocument>(Validator!) : null,
            ValidationLevel = validates ? ValidationLevel : null,
            ValidationAction = validates ? ValidationAction : null,
            ChangeStreamPreAndPostImagesOptions = ChangeStreamPreAndPostImages is true
                ? new ChangeStreamPreAndPostImagesOptions { Enabled = true }
                : null,
            ClusteredIndex = Clustered
                ? new ClusteredIndexOptions<BsonDocument> { Key = new BsonDocument("_id", 1), Unique = true }
                : null,
            ExpireAfter = Clustered ? ExpireAfter : null,
            EncryptedFields = encryptedFields
        };
    }

    /// <summary>How they're shown in logs, such as <c>collation { "locale" : "tr" }, clustered</c>.</summary>
    public string Definition =>
        string.Join(", ", new[]
        {
            Collation is null ? null : $"collation {StoredSpecs.Describe(Collation.ToBsonDocument())}",
            Validator is null ? null : $"validator {Validator.ToJson()} ({Level}, {Action})",
            ChangeStreamPreAndPostImages is true ? "change stream images" : null,
            Clustered ? "clustered" : null,
            Clustered && ExpireAfter is { } expiry ? $"expiring after {expiry.TotalSeconds:0} s" : null
        }.OfType<string>());

    /// <summary>The validation level as the server names it, such as <c>strict</c>.</summary>
    public string Level => ValidationLevel.ToString().ToLowerInvariant();

    /// <summary>The validation action as the server names it, such as <c>error</c>.</summary>
    public string Action => ValidationAction.ToString().ToLowerInvariant();
}
