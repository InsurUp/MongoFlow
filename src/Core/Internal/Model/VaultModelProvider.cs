namespace MongoFlow;

/// <summary>Builds a vault's model once, from the root provider, the first time it's needed.</summary>
internal sealed class VaultModelProvider<TVault> where TVault : MongoVault
{
    private readonly Lazy<VaultModel> _model;

    public VaultModelProvider(IServiceProvider services,
        VaultRegistration<TVault> registration,
        IEnumerable<DefaultVaultConfiguration> defaults)
    {
        _model = new Lazy<VaultModel>(() => Build(services, registration, defaults.ToList()));
    }

    public VaultModel Model => _model.Value;

    private static VaultModel Build(IServiceProvider services,
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
