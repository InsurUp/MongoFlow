using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A field a feature filters every read on, rendered, which MongoFlow maps in the collection's search indexes.</summary>
internal sealed record FeatureField(RenderedFieldDefinition Field,
    string Feature)
{
    public string Path => Field.FieldName;

    /// <summary>The mapping a search's filters compare it with, or <see langword="null"/> when none can.</summary>
    public string? SearchType { get; } = SearchFieldTypes.Of(Field.FieldSerializer);
}
