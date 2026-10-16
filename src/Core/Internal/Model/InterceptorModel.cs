using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

internal sealed class InterceptorModel(Type? type,
    VaultInterceptor? instance,
    FeatureKey? owner,
    IReadOnlySet<IVaultCollectionInfo> visible)
{
    public FeatureKey? Owner { get; } = owner;

    /// <summary>
    /// Whether the interceptor sees <paramref name="operation"/>: it's on a collection the interceptor applies to, decided at
    /// startup, and wasn't queued with the interceptor's feature switched off.
    /// </summary>
    public bool Sees(VaultOperation operation) => Sees(operation.Collection, operation.DisabledFeatures);

    /// <summary>
    /// Whether the interceptor sees what's of <paramref name="collection"/> with <paramref name="disabled"/> features off,
    /// such as a tracked document read with them off.
    /// </summary>
    public bool Sees(IVaultCollectionInfo collection,
        FeatureSet disabled) =>
        visible.Contains(collection) && (Owner is not { } owner || !disabled.Contains(owner));

    /// <summary>
    /// The shared instance, or one created from the request's <paramref name="services"/>; a disposable one is disposed
    /// with their scope.
    /// </summary>
    public VaultInterceptor Create(IServiceProvider services)
    {
        if (instance is not null)
        {
            return instance;
        }

        var created = (VaultInterceptor)ActivatorUtilities.CreateInstance(services, type!);
        services.GetRequiredService<OwnedInterceptors>().Track(created);

        return created;
    }
}
