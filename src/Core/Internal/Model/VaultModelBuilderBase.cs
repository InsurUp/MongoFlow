namespace MongoFlow;

internal abstract class VaultModelBuilderBase
{
    private readonly Stack<FeatureKey> _owners = new();
    private readonly List<InterceptorRegistration> _interceptors = [];

    /// <summary>The layer settings are recorded into; see <see cref="MongoFlow.Layer"/>.</summary>
    public Layer Layer { get; set; } = Layer.Own;

    /// <summary>The feature being configured, which owns the filters and interceptors added meanwhile.</summary>
    public FeatureKey? Owner => _owners.Count > 0 ? _owners.Peek() : null;

    protected IReadOnlyList<InterceptorRegistration> Interceptors => _interceptors;

    public void Register(Type? type, VaultInterceptor? instance, CollectionModelBuilder? collection,
        Action<IInterceptorBuilder>? configure)
    {
        var registration = new InterceptorRegistration
        {
            Layer = Layer,
            Order = _interceptors.Count,
            Type = type,
            Instance = instance,
            Collection = collection,
            Owner = Owner
        };

        configure?.Invoke(registration);
        _interceptors.Add(registration);
    }

    protected void PushOwner(FeatureKey feature) => _owners.Push(feature);

    protected void PopOwner() => _owners.Pop();
}
