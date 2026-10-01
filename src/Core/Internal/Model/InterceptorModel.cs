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
    public bool Sees(VaultOperation operation) =>
        visible.Contains(operation.Collection) &&
        (Owner is not { } owner || !operation.DisabledFeatures.Contains(owner));

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
