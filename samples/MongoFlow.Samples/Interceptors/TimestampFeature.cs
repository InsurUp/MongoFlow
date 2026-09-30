using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Interceptors;

/// <summary>
/// Stamps <see cref="ITimestamped"/> documents on every write. Registered per collection, so the interceptor knows the
/// document type and can add to update definitions, not just to documents in memory.
/// </summary>
public sealed class TimestampFeature : IVaultFeature, IVaultCollectionConfiguration
{
    public static FeatureKey Key { get; } = new("timestamps");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(ITimestamped)))
        {
            collection.AddInterceptor<TimestampInterceptor<TDocument>>();
        }
    }
}
