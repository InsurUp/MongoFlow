namespace MongoFlow;

internal abstract class VaultModelBuilderBase(IServiceProvider services)
{
    private readonly Stack<FeatureKey> _owners = new();
    private readonly List<InterceptorRegistration> _interceptors = [];
    private readonly Layered<VaultEncryption> _encryption = new();

    /// <summary>The root provider the vault is configured from.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>The layer settings are recorded into; see <see cref="MongoFlow.Layer"/>.</summary>
    public Layer Layer { get; set; } = Layer.Own;

    /// <summary>The feature being configured, which owns the filters and interceptors added meanwhile.</summary>
    public FeatureKey? Owner => _owners.Count > 0 ? _owners.Peek() : null;

    protected IReadOnlyList<InterceptorRegistration> Interceptors => _interceptors;

    /// <summary>The vault's encryption, or <see langword="null"/> when it doesn't encrypt.</summary>
    protected VaultEncryption? Encryption => _encryption.TryGet(out var encryption) ? encryption : null;

    /// <summary>Makes the vault encrypt, as MongoFlow.Encryption configures it. The vault's own setting wins over a default's.</summary>
    public void UseEncryption(VaultEncryption encryption) => _encryption.Set(Layer, encryption);

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
