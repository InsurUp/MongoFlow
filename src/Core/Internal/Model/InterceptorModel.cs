using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

internal sealed class InterceptorModel(Type? type,
    VaultInterceptor? instance,
    FeatureKey? owner,
    IReadOnlySet<IVaultCollectionInfo> visible)
{
    public FeatureKey? Owner { get; } = owner;

    /// <summary>Whether the interceptor sees operations on <paramref name="collection"/>, decided at startup.</summary>
    public bool AppliesTo(IVaultCollectionInfo collection) => visible.Contains(collection);

    public VaultInterceptor Create(IServiceProvider services) =>
        instance ?? (VaultInterceptor)ActivatorUtilities.CreateInstance(services, type!);
}
