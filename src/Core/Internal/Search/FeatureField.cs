using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A field a feature filters every read on, rendered, which MongoFlow maps in the collection's search indexes.</summary>
/// <param name="Field">The field, rendered.</param>
/// <param name="Feature">The feature's name.</param>
/// <param name="ComparedAs">
/// What the feature compares it as, such as <c>bool</c>, <c>DateTime?</c>, or <c>ObjectId?</c> for a tenant stored as an
/// <c>ObjectId</c>.
/// </param>
internal sealed record FeatureField(RenderedFieldDefinition Field,
    string Feature,
    Type ComparedAs)
{
    public string Path => Field.FieldName;

    /// <summary>The mapping a search's filters compare it with, or <see langword="null"/> when none can.</summary>
    public string? SearchType { get; } = SearchFieldTypes.Of(Field.FieldSerializer);

    /// <summary>
    /// Whether the feature can compare it with null, as it compares it as a type that can be null: soft delete's deletion
    /// date, which isn't set until deleted, and a tenant, which is null without a current one.
    /// </summary>
    public bool ComparesNull => !ComparedAs.IsValueType || Nullable.GetUnderlyingType(ComparedAs) is not null;
}
