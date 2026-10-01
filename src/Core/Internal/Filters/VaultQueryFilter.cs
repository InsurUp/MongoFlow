namespace MongoFlow;

/// <summary>A filter added on the vault, applied to every collection whose document is assignable to its target.</summary>
internal abstract class VaultQueryFilter(Layer layer, FeatureKey? owner)
{
    public Layer Layer { get; } = layer;

    public FeatureKey? Owner { get; } = owner;

    public abstract QueryFilterEntry<TDocument>? For<TDocument>();
}
