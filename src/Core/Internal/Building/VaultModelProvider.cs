using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

/// <summary>
/// Builds a vault's model once, from the root provider, the first time it's needed, and creates the vault's instances.
/// </summary>
internal sealed class VaultModelProvider<TVault> where TVault : MongoVault
{
    private readonly Lazy<VaultModel> _model;
    private readonly ObjectFactory<TVault> _createVault = ActivatorUtilities.CreateFactory<TVault>([]);

    public VaultModelProvider(IServiceProvider services,
        VaultRegistration<TVault> registration,
        IEnumerable<DefaultVaultConfiguration> defaults)
    {
        _model = new Lazy<VaultModel>(() => Build(services, registration, defaults.ToList()));
    }

    public VaultModel Model => _model.Value;

    /// <summary>Creates an instance from the request's services, with a factory compiled once.</summary>
    /// <remarks><c>ActivatorUtilities.CreateInstance</c> would look for the constructor every time, a sixth of a resolution.</remarks>
    public TVault CreateVault(IServiceProvider services) => _createVault(services, null);

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

        var model = builder.Build();

        model.Logs.Model.ModelBuilt(typeof(TVault).Name,
            model.Database.DatabaseNamespace.DatabaseName,
            model.Collections.Select(collection => collection.Namespace.CollectionName),
            model.Interceptors.Count);
        IndexCheck.Start(model);

        return model;
    }
}
