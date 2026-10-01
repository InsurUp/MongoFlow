namespace MongoFlow;

/// <summary>
/// Builds a vault's model once, from the root provider, the first time it's needed, and fills the collection properties
/// of each new instance: the one part of the model that needs the vault type.
/// </summary>
internal sealed class VaultModelProvider<TVault> where TVault : MongoVault
{
    private readonly Lazy<(VaultModel Model, IReadOnlyList<CollectionBinding<TVault>> Bindings)> _built;

    public VaultModelProvider(IServiceProvider services,
        VaultRegistration<TVault> registration,
        IEnumerable<DefaultVaultConfiguration> defaults)
    {
        _built = new Lazy<(VaultModel, IReadOnlyList<CollectionBinding<TVault>>)>(() =>
            Build(services, registration, defaults.ToList()));
    }

    public VaultModel Model => _built.Value.Model;

    /// <summary>Fills <paramref name="vault"/>'s collection properties with collections bound to <paramref name="runtime"/>.</summary>
    public void Attach(TVault vault, VaultRuntime runtime)
    {
        foreach (var binding in _built.Value.Bindings)
        {
            binding.Attach(vault, runtime);
        }
    }

    private static (VaultModel, IReadOnlyList<CollectionBinding<TVault>>) Build(IServiceProvider services,
        VaultRegistration<TVault> registration,
        IReadOnlyList<DefaultVaultConfiguration> defaults)
    {
        // The vault's own configuration runs first so its skips are known; see Layer for how defaults still come first.
        var builder = new VaultModelBuilder<TVault>(services) { Layer = Layer.Own };

        ConfigurableVault.Configure(builder);

        foreach (var configure in registration.Configurations)
        {
            configure(services, builder);
        }

        builder.Layer = Layer.Default;

        foreach (var configuration in defaults)
        {
            builder.ApplyDefault(configuration.Type);
        }

        return builder.Build();
    }
}
